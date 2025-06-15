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
        extend: {
            boxShadow: {
                neumorphic: '10px 10px 30px #d1d9e6, -10px -10px 30px #ffffff',
                'neumorphic-inset': 'inset 10px 10px 20px #d1d9e6, inset -10px -10px 20px #ffffff',
                'neumorphic-light': '4px 4px 10px #c8d0e7, -4px -4px 10px #ffffff',
            },
            borderRadius: {
                '3xl': '2rem',
            }
        },
    },
    plugins: [],
}
