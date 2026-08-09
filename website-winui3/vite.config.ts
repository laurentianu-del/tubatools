import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-vue';

// https://vitejs.dev/config/
export default defineConfig({
    base: '/',
    plugins: [plugin()],
    server: {
        port: 63179,
    }
})
