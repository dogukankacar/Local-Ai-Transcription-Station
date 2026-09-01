/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        ink: {
          bg: "#12141A",
          panel: "#1A1D24",
          panelAlt: "#20242D",
          border: "#2A2F3A",
        },
        paper: {
          DEFAULT: "#E9E7E0",
          muted: "#8D919C",
        },
        stamp: {
          pending: "#C9A227",
          completed: "#3FA796",
          failed: "#B3432B",
          redaction: "#0A0A0A",
        },
      },
      fontFamily: {
        stamp: ["IBM Plex Mono", "monospace"],
        body: ["IBM Plex Sans", "sans-serif"],
      },
      keyframes: {
        stampIn: {
          "0%": { opacity: "0", transform: "scale(1.6) rotate(-8deg)" },
          "60%": { opacity: "1", transform: "scale(0.92) rotate(3deg)" },
          "100%": { opacity: "1", transform: "scale(1) rotate(-2deg)" },
        },
        pulseSlow: {
          "0%, 100%": { opacity: "1" },
          "50%": { opacity: "0.55" },
        },
      },
      animation: {
        "stamp-in": "stampIn 420ms cubic-bezier(0.34, 1.56, 0.64, 1) forwards",
        "pulse-slow": "pulseSlow 1.8s ease-in-out infinite",
      },
    },
  },
  plugins: [],
};
