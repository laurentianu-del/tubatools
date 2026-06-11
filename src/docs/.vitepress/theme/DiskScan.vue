<template>
  <div class="disk-scan">
    <div class="scan-content">
      <div class="scan-visual">
        <div class="disk-frame">
          <div class="scan-header">
            <span class="scan-title"><i class="fa-solid fa-chart-pie"></i> 磁盘空间分析</span>
            <span class="scan-progress-label">100%</span>
          </div>
          <div class="scan-bar-track"><div class="scan-bar-fill"></div></div>
          <div class="folder-grid">
            <div v-for="(folder, i) in folders" :key="i" class="folder-block" :style="{ backgroundColor: folder.color, gridRow: folder.row, gridColumn: folder.col }">
              <i :class="folder.icon"></i>
              <span class="f-name">{{ folder.name }}</span>
              <span class="f-size">{{ folder.size }}</span>
            </div>
          </div>
        </div>
      </div>
      <div class="scan-text">
        <div class="section-badge"><i class="fa-solid fa-chart-pie"></i> 空间分析</div>
        <h3>可视化磁盘空间分析</h3>
        <p>扫描磁盘空间占用，用彩色方块图直观展示每个文件夹的大小。快速定位大文件，释放磁盘空间，告别存储焦虑。</p>
        <div class="scan-features">
          <div class="scan-feature"><i class="fa-solid fa-bolt"></i> 超快 MFT 扫描</div>
          <div class="scan-feature"><i class="fa-solid fa-chart-pie"></i> 树状图可视化</div>
          <div class="scan-feature"><i class="fa-solid fa-magnifying-glass"></i> 大文件定位</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
const folders = [
  { name: 'Games', size: '67.8 GB', icon: 'fa-solid fa-gamepad', color: '#0078d4', row: '1/3', col: '1/3' },
  { name: 'Users', size: '45.2 GB', icon: 'fa-solid fa-user', color: '#107c10', row: '1/2', col: '3/5' },
  { name: 'Windows', size: '24.3 GB', icon: 'fa-brands fa-windows', color: '#005a9e', row: '3/5', col: '1/3' },
  { name: 'Program Files', size: '18.7 GB', icon: 'fa-solid fa-folder', color: '#8764b8', row: '2/4', col: '3/5' },
  { name: 'Updates', size: '8.5 GB', icon: 'fa-solid fa-download', color: '#ca5010', row: '5/6', col: '1/2' },
  { name: 'Temp', size: '3.1 GB', icon: 'fa-solid fa-fire', color: '#d83b01', row: '4/5', col: '3/4' },
  { name: 'Drivers', size: '2.4 GB', icon: 'fa-solid fa-microchip', color: '#008272', row: '4/5', col: '4/5' },
  { name: 'Recovery', size: '1.8 GB', icon: 'fa-solid fa-rotate-left', color: '#a4262c', row: '5/6', col: '2/4' },
]
</script>

<style scoped>
.disk-scan { padding: 48px 0; }
.scan-content { display: flex; gap: 48px; align-items: center; }
.scan-visual { flex: 1.3; min-width: 320px; }
.disk-frame { border-radius: 8px; overflow: hidden; border: 1px solid var(--fluent-stroke); background: var(--vp-c-bg-soft); box-shadow: var(--fluent-shadow); }
.scan-header { display: flex; align-items: center; justify-content: space-between; padding: 12px 16px; border-bottom: 1px solid var(--fluent-stroke); background: var(--vp-c-bg-soft); }
.scan-title { font-size: 13px; font-weight: 600; display: flex; align-items: center; gap: 8px; }
.scan-title i { color: var(--vp-c-brand-1); }
.scan-progress-label { font-size: 13px; font-weight: 700; color: var(--vp-c-brand-1); font-variant-numeric: tabular-nums; }
.scan-bar-track { height: 3px; background: var(--fluent-stroke); }
.scan-bar-fill { height: 100%; width: 100%; background: var(--vp-c-brand-1); }
.folder-grid { display: grid; grid-template-columns: repeat(4, 1fr); grid-template-rows: repeat(5, 1fr); gap: 3px; padding: 3px; min-height: 300px; }
.folder-block { display: flex; flex-direction: column; align-items: center; justify-content: center; border-radius: 4px; padding: 8px; color: white; }
.folder-block i { font-size: 20px; margin-bottom: 4px; }
.f-name { font-size: 11px; font-weight: 600; white-space: nowrap; }
.f-size { font-size: 10px; opacity: 0.85; }
.scan-text { flex: 1; min-width: 280px; }
.section-badge { display: inline-flex; align-items: center; gap: 6px; padding: 5px 14px; border-radius: 6px; font-size: 13px; font-weight: 600; background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1); margin-bottom: 16px; border: 1px solid var(--fluent-stroke); }
.scan-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.scan-text p { font-size: 16px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.scan-features { display: flex; flex-direction: column; gap: 10px; }
.scan-feature { display: flex; align-items: center; gap: 10px; padding: 10px 16px; background: var(--vp-c-bg-soft); border: 1px solid var(--fluent-stroke); border-radius: 6px; font-size: 14px; font-weight: 500; transition: border-color 0.15s; }
.scan-feature:hover { border-color: var(--vp-c-brand-1); }
.scan-feature i { color: var(--vp-c-brand-1); width: 18px; text-align: center; }
@media (max-width: 768px) { .scan-content { flex-direction: column; } .scan-text h3 { font-size: 22px; } }
</style>
