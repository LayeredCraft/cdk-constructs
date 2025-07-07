// Test Static Site JavaScript

document.addEventListener('DOMContentLoaded', function() {
    console.log('Test static site loaded successfully');
    
    // Add some basic interactivity for testing
    const navLinks = document.querySelectorAll('nav a');
    navLinks.forEach(link => {
        link.addEventListener('click', function(e) {
            console.log('Navigation link clicked:', this.href);
        });
    });
    
    // Test API functionality if available
    if (window.location.pathname.includes('/api/')) {
        console.log('API route detected');
    }
    
    // Simple feature detection
    const features = {
        localStorage: typeof(Storage) !== "undefined",
        fetch: typeof(fetch) !== "undefined",
        webGL: !!window.WebGLRenderingContext
    };
    
    console.log('Browser features:', features);
});