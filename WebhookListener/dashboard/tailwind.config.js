/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        error: "#ffb4ab",
        "on-primary-container": "#005025",
        "surface-container-high": "#282a2a",
        "on-secondary-fixed": "#002116",
        "inverse-surface": "#e2e2e2",
        outline: "#879487",
        "secondary-fixed-dim": "#a0d1bc",
        "on-tertiary": "#003827",
        tertiary: "#92ddbb",
        "secondary-container": "#235141",
        "on-background": "#e2e2e2",
        "primary-fixed": "#83fba5",
        "on-primary-fixed": "#00210c",
        "on-surface-variant": "#bdcabc",
        "on-secondary-container": "#93c3ae",
        "error-container": "#93000a",
        "on-tertiary-container": "#004f38",
        "surface-container-low": "#1a1c1c",
        "on-secondary-fixed-variant": "#214f3f",
        "on-primary-fixed-variant": "#005227",
        surface: "#121414",
        "on-error": "#690005",
        "outline-variant": "#3e4a3f",
        secondary: "#a0d1bc",
        "tertiary-fixed": "#a6f2cf",
        "on-tertiary-fixed": "#002115",
        "surface-tint": "#66dd8b",
        "on-secondary": "#043829",
        "surface-container-lowest": "#0d0f0f",
        "tertiary-fixed-dim": "#8bd6b4",
        "surface-container-highest": "#333535",
        "surface-variant": "#333535",
        "tertiary-container": "#77c1a0",
        "on-surface": "#e2e2e2",
        "inverse-primary": "#006d36",
        background: "#121414",
        "primary-container": "#50c878",
        "on-error-container": "#ffdad6",
        "on-tertiary-fixed-variant": "#00513a",
        "surface-bright": "#383939",
        "surface-dim": "#121414",
        "secondary-fixed": "#bcedd7",
        primary: "#6ee591",
        "inverse-on-surface": "#2f3131",
        "on-primary": "#003919",
        "surface-container": "#1e2020",
        "primary-fixed-dim": "#66dd8b"
      },
      borderRadius: {
        DEFAULT: "0.125rem",
        lg: "0.25rem",
        xl: "0.5rem",
        full: "0.75rem"
      },
      spacing: {
        xl: "80px",
        margin: "32px",
        lg: "48px",
        xs: "8px",
        sm: "16px",
        base: "4px",
        md: "24px",
        gutter: "20px"
      },
      fontFamily: {
        "headline-lg": ["Space Grotesk", "sans-serif"],
        "label-caps": ["JetBrains Mono", "monospace"],
        "body-md": ["Sora", "sans-serif"],
        "headline-lg-mobile": ["Space Grotesk", "sans-serif"],
        "label-mono": ["JetBrains Mono", "monospace"],
        "display-lg": ["Space Grotesk", "sans-serif"]
      },
      fontSize: {
        "headline-lg": ["32px", { lineHeight: "1.2", fontWeight: "600" }],
        "label-caps": ["12px", { lineHeight: "1.0", letterSpacing: "0.1em", fontWeight: "700" }],
        "body-md": ["16px", { lineHeight: "1.6", fontWeight: "400" }],
        "headline-lg-mobile": ["24px", { lineHeight: "1.2", fontWeight: "600" }],
        "label-mono": ["14px", { lineHeight: "1.4", letterSpacing: "0.05em", fontWeight: "500" }],
        "display-lg": ["48px", { lineHeight: "1.1", letterSpacing: "-0.02em", fontWeight: "700" }]
      }
    },
  },
  plugins: [],
}

