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
            boxShadow: {
                neumorphic: '10px 10px 30px #d1d9e6, -10px -10px 30px #ffffff',
                'neumorphic-inset': 'inset 10px 10px 20px #d1d9e6, inset -10px -10px 20px #ffffff',
                'neumorphic-light': '4px 4px 10px #c8d0e7, -4px -4px 10px #ffffff',
            },
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
