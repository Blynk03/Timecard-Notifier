/** @type {import('tailwindcss').Config} */
// Keep this theme in sync with the CDN fallback config in wwwroot/index.html.
module.exports = {
  content: ['./**/*.razor', './**/*.cs', './wwwroot/index.html'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['"IBM Plex Sans"', 'system-ui', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'ui-monospace', 'monospace'],
      },
      colors: {
        brand: {
          50: '#eef5f8', 100: '#d6e6ee', 200: '#adcddd', 300: '#7eaec5',
          400: '#4f8ba9', 500: '#2f6f8f', 600: '#245a75', 700: '#1d4a60',
          800: '#173b4c', 900: '#0f2a38',
        },
        teams: { DEFAULT: '#5b5fc7', dark: '#4f52b2', light: '#e8ebfa' },
      },
    },
  },
  plugins: [],
};
