import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'
import fs from 'node:fs'

export default defineConfig(({ mode }) => {
  const isMock = mode === 'mock' || process.env.VITE_USE_MOCK === 'true' || process.env.NODE_ENV === 'test'

  return {
    plugins: [
      vue(),
      tailwindcss(),
      {
        name: 'preserve-gitkeep',
        closeBundle() {
          const targetDir = path.resolve(__dirname, '../src/Shipit.Api/wwwroot')
          const gitkeepPath = path.resolve(targetDir, '.gitkeep')
          if (fs.existsSync(targetDir) && !fs.existsSync(gitkeepPath)) {
            fs.writeFileSync(gitkeepPath, '')
          }
        }
      }
    ],
    define: {
      ...(isMock ? { 'import.meta.env.VITE_USE_MOCK': JSON.stringify('true') } : {})
    },
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src')
      }
    },
    build: {
      outDir: '../src/Shipit.Api/wwwroot',
      emptyOutDir: false
    },
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: 'http://localhost:5000',
          changeOrigin: true
        },
        '/healthz': {
          target: 'http://localhost:5000',
          changeOrigin: true
        }
      }
    },
    test: {
      environment: 'jsdom',
      globals: true
    }
  } as any
})
