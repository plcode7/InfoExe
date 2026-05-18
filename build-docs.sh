#!/bin/bash

# Docfx documentation build script

echo "Building InfoExe documentation with docfx..."

# Generate metadata from .NET projects
echo "Generating API metadata..."
docfx metadata docfx.json

# Build HTML documentation
echo "Building HTML documentation..."
docfx build docfx.json

# Copy index.html to root
echo "Copying index.html to root..."
cp docs/_site/docs/index.html docs/_site/index.html

echo "Documentation built successfully!"
echo "Serve with: docfx serve docs/_site --port 8080"