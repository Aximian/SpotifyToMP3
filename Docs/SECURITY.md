# Security Policy

## Supported Versions

We actively support the latest version of Spotify to MP3 Converter. Security updates will be provided for the current release.

## Reporting a Vulnerability

If you discover a security vulnerability, please **do not** open a public issue. Instead, please email the maintainers directly or create a private security advisory.

### What to Report

Please report:
- Authentication or authorization flaws
- Code injection vulnerabilities
- Sensitive data exposure
- Security misconfigurations
- Any other security-related issues

### What NOT to Report

Please do not report:
- Issues related to downloading copyrighted content (this is a legal/ToS issue, not a security issue)
- Feature requests
- General bugs (use regular issues for these)

## Security Best Practices

### For Users

1. **Never share your Spotify API credentials**
   - Keep your Client ID and Client Secret private
   - Use environment variables instead of hardcoding credentials
   - Rotate your credentials if they're accidentally exposed

2. **Keep dependencies updated**
   - Regularly update yt-dlp: `yt-dlp --update`
   - Keep FFmpeg updated
   - Update the application when new versions are released

3. **Use trusted sources**
   - Only download the application from the official repository
   - Verify checksums if provided
   - Be cautious of third-party builds

### For Developers

1. **Never commit credentials**
   - Use environment variables or secure configuration files
   - Add credential files to `.gitignore`
   - Review code before committing

2. **Handle sensitive data carefully**
   - Don't log API keys or secrets
   - Use secure storage for user settings
   - Implement proper error handling

3. **Keep dependencies secure**
   - Regularly update NuGet packages
   - Monitor for security advisories
   - Use dependency scanning tools

## Security Updates

Security updates will be released as soon as possible after a vulnerability is discovered and patched. We recommend:

- Keeping the application updated
- Monitoring the repository for security advisories
- Following security best practices

Thank you for helping keep Spotify to MP3 Converter secure!

