# Contributing to Spotify to MP3 Converter

Thank you for your interest in contributing to Spotify to MP3 Converter! This document provides guidelines and instructions for contributing.

## Getting Started

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/YOUR_USERNAME/SpotifyToMP3.git
   cd SpotifyToMP3
   ```
3. **Create a new branch** for your feature or bugfix:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Setup

1. **Install .NET 8.0 SDK** or later
2. **Get Spotify API Credentials** (see README.md for instructions)
3. **Configure credentials** - Use environment variables or create a local config file (see README.md)
4. **Restore dependencies**:
   ```bash
   dotnet restore
   ```
5. **Build the project**:
   ```bash
   dotnet build
   ```

## Making Changes

1. **Write clean, readable code** following C# conventions
2. **Add comments** for complex logic
3. **Test your changes** thoroughly before submitting
4. **Update documentation** if you add new features or change behavior

## Submitting Changes

1. **Commit your changes** with clear, descriptive commit messages:
   ```bash
   git commit -m "Add feature: description of what you added"
   ```
2. **Push to your fork**:
   ```bash
   git push origin feature/your-feature-name
   ```
3. **Create a Pull Request** on GitHub with:
   - A clear title and description
   - Reference to any related issues
   - Screenshots if UI changes are involved

## Code Style Guidelines

- Use meaningful variable and method names
- Follow C# naming conventions (PascalCase for public members, camelCase for private)
- Add XML comments for public methods
- Keep methods focused and single-purpose
- Handle errors gracefully with try-catch blocks

## Reporting Issues

When reporting bugs, please include:
- **Description** of the issue
- **Steps to reproduce** the problem
- **Expected behavior** vs **Actual behavior**
- **Environment**: OS version, .NET version
- **Screenshots** if applicable

## Feature Requests

For feature requests, please:
- Describe the feature clearly
- Explain why it would be useful
- Provide examples of how it would work

## Questions?

Feel free to open an issue for any questions or discussions!

Thank you for contributing! 🎵

