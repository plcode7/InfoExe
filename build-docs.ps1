# Docfx documentation build script

Write-Host "Building InfoExe documentation with docfx..." -ForegroundColor Green

# Generate metadata from .NET projects
Write-Host "Generating API metadata..." -ForegroundColor Yellow
docfx metadata docfx.json

# Build HTML documentation
Write-Host "Building HTML documentation..." -ForegroundColor Yellow
docfx build docfx.json

# Copy index.html to root
Write-Host "Copying index.html to root..." -ForegroundColor Yellow
Copy-Item docs/_site/docs/index.html docs/_site/index.html

Write-Host "Documentation built successfully!" -ForegroundColor Green
Write-Host "Serve with: docfx serve docs/_site --port 8080" -ForegroundColor Cyan