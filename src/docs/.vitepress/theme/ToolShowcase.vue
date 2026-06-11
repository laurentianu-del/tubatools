<template>
  <div class="tool-showcase">
    <div class="showcase-content">
      <div class="showcase-text">
        <div class="section-badge"><i class="fa-solid fa-rocket"></i> 工具目录</div>
        <h3>94 款专业工具，一键启动</h3>
        <p>自动扫描本地工具目录，按处理器、显卡、硬盘、内存等 8 大分类整齐归档。支持实时搜索、收藏夹、管理员运行、创建桌面快捷方式。</p>
        <div class="tool-tags">
          <span v-for="tag in tags" :key="tag" class="tool-tag">{{ tag }}</span>
        </div>
      </div>
      <div class="showcase-image-wrap">
        <div class="image-shell">
          <div class="image-frame">
            <div class="frame-bar">
              <span class="frame-dot r"></span>
              <span class="frame-dot y"></span>
              <span class="frame-dot g"></span>
            </div>
            <img src="/screenshot-tools.png" alt="图吧工具箱界面" class="showcase-img" />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
const tags = ['CPU-Z', 'GPU-Z', 'FurMark', 'CrystalDiskMark', 'AIDA64', 'HWiNFO', 'DiskGenius', 'Everything']
</script>

<style scoped>
.tool-showcase { padding: 48px 0; }
.showcase-content { display: flex; gap: 48px; align-items: center; }
.showcase-text { flex: 1; min-width: 280px; }
.section-badge {
  display: inline-flex; align-items: center; gap: 6px; padding: 4px 12px;
  border-radius: 6px; font-size: 12px; font-weight: 600;
  background: var(--vp-c-brand-soft); color: var(--vp-c-brand-1);
  border: 1px solid var(--fluent-stroke); margin-bottom: 16px;
}
.showcase-text h3 { font-size: 28px; font-weight: 700; margin: 0 0 16px; line-height: 1.3; }
.showcase-text p { font-size: 15px; color: var(--vp-c-text-2); line-height: 1.7; margin: 0 0 24px; }
.tool-tags { display: flex; flex-wrap: wrap; gap: 8px; }
.tool-tag {
  padding: 5px 12px; border-radius: 6px; font-size: 12px; font-weight: 500;
  background: var(--vp-c-bg-soft); color: var(--vp-c-text-1);
  border: 1px solid var(--fluent-stroke);
  transition: border-color 0.2s cubic-bezier(0.33, 0, 0.2, 1), box-shadow 0.2s cubic-bezier(0.33, 0, 0.2, 1);
}
.tool-tag:hover { border-color: var(--vp-c-brand-1); box-shadow: 0 0 0 1px var(--vp-c-brand-1); }
.showcase-image-wrap { flex: 1.2; min-width: 320px; }
.image-shell {
  padding: 4px;
  border-radius: 14px;
  background: var(--vp-c-bg-soft);
  border: 1px solid var(--fluent-stroke);
  box-shadow: var(--fluent-shadow-card);
  transition: box-shadow 0.25s cubic-bezier(0.33, 0, 0.2, 1);
}
.image-shell:hover {
  box-shadow: var(--fluent-shadow-card-hover);
}
.image-frame { border-radius: 10px; overflow: hidden; background: var(--vp-c-bg-soft); }
.frame-bar {
  display: flex; gap: 6px; padding: 10px 14px;
  background: var(--vp-c-bg-soft); border-bottom: 1px solid var(--fluent-stroke);
}
.frame-dot { width: 10px; height: 10px; border-radius: 50%; }
.frame-dot.r { background: #ff5f57; } .frame-dot.y { background: #febc2e; } .frame-dot.g { background: #28c840; }
.showcase-img { width: 100%; display: block; }
@media (max-width: 768px) { .showcase-content { flex-direction: column; } .showcase-text h3 { font-size: 22px; } }
</style>
