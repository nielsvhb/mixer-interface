const colors = require('tailwindcss/colors')

module.exports = {
    safelist: [],
    content: [
        "./**/*.razor",
        "./wwwroot/index.html",
        "./Pages/**/*.razor",
        "./Shared/**/*.razor"
    ],
    theme: {
        container: {
          center: true,
          padding: '1rem'  
        },
        colors: {
            transparent: 'transparent',
            current: 'currentColor',
            black: colors.black,
            white: colors.white,
            gray: colors.slate,
            primary: "#FF6666",
            primaryDark: "#FF5c7D",
            accent: "#E1CE55"
        },
        extend: {
            borderRadius: {
                '3xl': '2rem',
            },
            fontFamily: {
                fredoka: ['"Fredoka"', 'sans-serif'],
            },
        },
    },
    plugins: [],
}
