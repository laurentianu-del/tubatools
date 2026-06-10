using System.Diagnostics;
using System.Runtime.InteropServices;
using Win32.Graphics.Direct3D;
using Win32.Graphics.Direct3D11;
using Win32.Graphics.Dxgi;
using Win32.Graphics.Dxgi.Common;
using static Win32.Graphics.Direct3D11.Apis;
using D3D11Usage = Win32.Graphics.Direct3D11.Usage;

namespace TubaWinUi3.Services;

public sealed unsafe class GpuComputeStress : IDisposable
{
    private const int MatrixSize = 512;
    private const int ThreadGroupSize = 16;
    private const int MinBatch = 4;
    private const int MaxBatch = 512;

    private ID3D11Device* _device;
    private ID3D11DeviceContext* _context;
    private ID3D11ComputeShader* _shader;
    private ID3D11Buffer* _bufferA;
    private ID3D11Buffer* _bufferB;
    private ID3D11Buffer* _bufferC;
    private ID3D11ShaderResourceView* _srvA;
    private ID3D11ShaderResourceView* _srvB;
    private ID3D11UnorderedAccessView* _uavC;

    private Thread? _thread;
    private volatile bool _running;
    private long _iterCount;
    private long _lastTs;
    private float _gflops;
    private int _batchSize = 32;
    private volatile float _gpuLoad;
    private long _adaptTs;
    private int _adaptInterval = 3;

    private static byte[]? _shaderBytes;

    [DllImport("d3dcompiler_47.dll", PreserveSig = true)]
    private static extern int D3DCompile(
        [In] IntPtr pSrcData, [In] nuint SrcDataSize,
        [In, MarshalAs(UnmanagedType.LPStr)] string? pSourceName,
        [In] IntPtr pDefines, [In] IntPtr pInclude,
        [In, MarshalAs(UnmanagedType.LPStr)] string pEntrypoint,
        [In, MarshalAs(UnmanagedType.LPStr)] string pTarget,
        [In] uint Flags1, [In] uint Flags2,
        out IntPtr ppCode, out IntPtr ppErrorMsgs);

    private static byte[] BuildShader()
    {
        var hlsl = @"cbuffer Constants : register(b0)
{
    uint N;
    uint Pad0;
    uint Pad1;
    uint Pad2;
};

StructuredBuffer<float> A : register(t0);
StructuredBuffer<float> B : register(t1);
RWStructuredBuffer<float> C : register(u0);

[numthreads(16, 16, 1)]
void CS(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= N || id.y >= N) return;
    float s = 0;
    for (uint k = 0; k < N; k++)
        s += A[id.y * N + k] * B[k * N + id.x];
    C[id.y * N + id.x] = s;
}";

        var h = System.Text.Encoding.ASCII.GetBytes(hlsl);
        IntPtr pCode = IntPtr.Zero, pErr = IntPtr.Zero;

        fixed (byte* ph = h)
        {
            var hr = D3DCompile(
                (nint)ph, (nuint)h.Length, "stress.hlsl",
                IntPtr.Zero, IntPtr.Zero, "CS", "cs_5_0",
                0, 0, out pCode, out pErr);
            if (hr < 0 || pCode == IntPtr.Zero)
            {
                string errMsg;
                if (pErr != IntPtr.Zero)
                {
                    var errBlob = (void*)pErr;
                    var errVt = *(void***)errBlob;
                    var errGetBuf = (delegate* unmanaged[Stdcall]<void*, void*>)(errVt[3]);
                    var errGetLen = (delegate* unmanaged[Stdcall]<void*, nuint>)(errVt[4]);
                    var errRel = (delegate* unmanaged[Stdcall]<void*, uint>)(errVt[2]);
                    var errBufPtr = errGetBuf(errBlob);
                    var errBufLen = (int)errGetLen(errBlob);
                    errMsg = errBufLen > 0 && errBufPtr != null
                        ? Marshal.PtrToStringAnsi((nint)errBufPtr, Math.Min(errBufLen, 1024)) ?? $"0x{(uint)hr:X8}"
                        : $"0x{(uint)hr:X8}";
                    errRel(errBlob);
                }
                else errMsg = $"0x{(uint)hr:X8}";
                throw new Exception($"D3DCompile 0x{(uint)hr:X8}: {errMsg}");
            }
        }

        var blob = (void*)pCode;
        var vt = *(void***)blob;
        var getBuf = (delegate* unmanaged[Stdcall]<void*, void*>)(vt[3]);
        var getLen = (delegate* unmanaged[Stdcall]<void*, nuint>)(vt[4]);
        var rel = (delegate* unmanaged[Stdcall]<void*, uint>)(vt[2]);
        var ptr = getBuf(blob);
        var len = (int)getLen(blob);
        var result = new byte[len];
        Marshal.Copy((nint)ptr, result, 0, len);
        rel(blob);
        return result;
    }

    public void Initialize()
    {
        _shaderBytes ??= BuildShader();

        ID3D11Device* dev = null;
        ID3D11DeviceContext* ctx = null;
        FeatureLevel fl;

        var hr = D3D11CreateDevice(null, DriverType.Hardware, 0,
            CreateDeviceFlags.None,
            null, 0, 7u, &dev, &fl, &ctx);
        if (hr < 0 || dev == null) throw new Exception($"D3D11CreateDevice: 0x{(uint)hr:X8}");
        _device = dev;
        _context = ctx;

        fixed (byte* ps = _shaderBytes!)
        {
            ID3D11ComputeShader* cs = null;
            hr = dev->CreateComputeShader(ps, (nuint)_shaderBytes!.Length, null, &cs);
            if (hr < 0 || cs == null) throw new Exception($"CreateComputeShader: 0x{(uint)hr:X8}");
            _shader = cs;
        }

        var elem = MatrixSize * MatrixSize;
        var bytes = elem * sizeof(float);
        var rng = new Random();
        var init = new float[elem];
        for (int i = 0; i < init.Length; i++) init[i] = (float)rng.NextDouble();

        var bd = new BufferDescription
        {
            ByteWidth = (uint)bytes,
            Usage = D3D11Usage.Default,
            BindFlags = BindFlags.ShaderResource,
            MiscFlags = ResourceMiscFlags.BufferStructured,
            StructureByteStride = sizeof(uint)
        };

        fixed (float* pd = init)
        {
            var sd = new SubresourceData { pSysMem = pd };
            ID3D11Buffer* ba = null; ID3D11Buffer* bb = null;
            dev->CreateBuffer(&bd, &sd, &ba);
            dev->CreateBuffer(&bd, &sd, &bb);
            _bufferA = ba; _bufferB = bb;
        }

        var bdc = new BufferDescription
        {
            ByteWidth = (uint)bytes,
            Usage = D3D11Usage.Default,
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
            MiscFlags = ResourceMiscFlags.BufferStructured,
            StructureByteStride = sizeof(uint)
        };
        ID3D11Buffer* bc = null;
        dev->CreateBuffer(&bdc, null, &bc);
        _bufferC = bc;

        var srv = new ShaderResourceViewDescription
        {
            Format = Format.R32Float,
            ViewDimension = SrvDimension.Buffer,
        };
        srv.Anonymous.Buffer.Anonymous1.FirstElement = 0;
        srv.Anonymous.Buffer.Anonymous2.NumElements = (uint)elem;

        ID3D11ShaderResourceView* sa = null; ID3D11ShaderResourceView* sb = null;
        dev->CreateShaderResourceView((ID3D11Resource*)_bufferA, &srv, &sa);
        dev->CreateShaderResourceView((ID3D11Resource*)_bufferB, &srv, &sb);
        _srvA = sa; _srvB = sb;

        var uav = new UnorderedAccessViewDescription
        {
            Format = Format.R32Float,
            ViewDimension = UavDimension.Buffer,
        };
        uav.Anonymous.Buffer.FirstElement = 0;
        uav.Anonymous.Buffer.NumElements = (uint)elem;
        uav.Anonymous.Buffer.Flags = BufferUavFlags.None;

        ID3D11UnorderedAccessView* uc = null;
        dev->CreateUnorderedAccessView((ID3D11Resource*)_bufferC, &uav, &uc);
        _uavC = uc;
    }

    public void SetGpuLoad(float load) => _gpuLoad = load;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _iterCount = 0;
        _lastTs = Stopwatch.GetTimestamp();
        _adaptTs = _lastTs;
        _batchSize = 32;
        _gpuLoad = 0;
        _thread = new Thread(Loop) { Name = "GPU-CS", IsBackground = true, Priority = ThreadPriority.Normal };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _thread?.Join(500); } catch { }
        if (_thread != null && _thread.IsAlive)
        {
            try { _thread.Interrupt(); } catch { }
        }
        _thread = null;
    }

    private void Loop()
    {
        var ctx = _context;
        var groups = MatrixSize / ThreadGroupSize;
        var loopCount = 0;

        ID3D11ShaderResourceView** srvs = stackalloc ID3D11ShaderResourceView*[2];
        ID3D11UnorderedAccessView** uavs = stackalloc ID3D11UnorderedAccessView*[1];

        ctx->CSSetShader(_shader, null, 0);
        srvs[0] = _srvA;
        srvs[1] = _srvB;
        ctx->CSSetShaderResources(0, 2, srvs);
        uavs[0] = _uavC;
        ctx->CSSetUnorderedAccessViews(0, 1, uavs, null);

        while (_running)
        {
            var batch = _batchSize;
            for (int i = 0; i < batch; i++)
            {
                ctx->Dispatch((uint)groups, (uint)groups, 1);
            }

            _iterCount += batch;
            loopCount++;

            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastTs) / (double)Stopwatch.Frequency;
            if (elapsed >= 1.0)
            {
                var ops = _iterCount * 2.0 * MatrixSize * MatrixSize * MatrixSize;
                _gflops = (float)(ops / elapsed / 1e9);
                _iterCount = 0;
                _lastTs = now;
            }

            var adaptElapsed = (now - _adaptTs) / (double)Stopwatch.Frequency;
            if (adaptElapsed >= _adaptInterval)
            {
                _adaptTs = now;
                AdaptLoad();
            }
        }
    }

    private void AdaptLoad()
    {
        var load = _gpuLoad;

        if (load >= 98) return;

        if (load >= 85)
        {
            _batchSize = Math.Min(_batchSize + 4, MaxBatch);
        }
        else if (load >= 70)
        {
            _batchSize = Math.Min(_batchSize + 16, MaxBatch);
        }
        else if (load >= 50)
        {
            _batchSize = Math.Min(_batchSize + 32, MaxBatch);
        }
        else
        {
            _batchSize = Math.Min(_batchSize + 64, MaxBatch);
        }
    }

    public float GetGflops() => _gflops;
    public int GetBatchSize() => _batchSize;

    public void Dispose()
    {
        Stop();
        if (_uavC != null) { _uavC->Release(); _uavC = null; }
        if (_srvA != null) { _srvA->Release(); _srvA = null; }
        if (_srvB != null) { _srvB->Release(); _srvB = null; }
        if (_bufferC != null) { _bufferC->Release(); _bufferC = null; }
        if (_bufferB != null) { _bufferB->Release(); _bufferB = null; }
        if (_bufferA != null) { _bufferA->Release(); _bufferA = null; }
        if (_shader != null) { _shader->Release(); _shader = null; }
        if (_context != null) { _context->Release(); _context = null; }
        if (_device != null) { _device->Release(); _device = null; }
    }
}
