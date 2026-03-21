// md4x WASM 加载器
// md4x 是高性能的 Markdown 解析库，比 Markdig 快 6 倍以上

let md4xModule = null;
let initPromise = null;

async function ensureInit() {
    if (md4xModule) return md4xModule;
    if (initPromise) return initPromise;

    initPromise = (async () => {
        try {
            const module = await import('https://esm.sh/md4x');
            await module.init();
            md4xModule = module;
            return module;
        } catch (error) {
            console.error('Failed to load md4x WASM:', error);
            initPromise = null;
            throw error;
        }
    })();

    return initPromise;
}

// 渲染 Markdown 为 HTML
window.md4x = {
    renderToHtml: async function(markdown) {
        if (!markdown) return '';
        try {
            const module = await ensureInit();
            return module.renderToHtml(markdown);
        } catch (error) {
            console.error('md4x renderToHtml failed:', error);
            // Fallback: 返回原始内容
            return markdown;
        }
    },

    // 修复不完整的 Markdown（用于流式渲染）
    heal: async function(incomplete) {
        if (!incomplete) return '';
        try {
            const module = await ensureInit();
            return module.heal(incomplete);
        } catch (error) {
            console.error('md4x heal failed:', error);
            return incomplete;
        }
    },

    // 渲染为纯文本
    renderToText: async function(markdown) {
        if (!markdown) return '';
        try {
            const module = await ensureInit();
            return module.renderToText(markdown);
        } catch (error) {
            console.error('md4x renderToText failed:', error);
            return markdown;
        }
    },

    // 检查是否已初始化
    isReady: function() {
        return md4xModule !== null;
    }
};
