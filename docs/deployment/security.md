# Security Best Practices

Security considerations and hardening for Tempo deployments.

## Overview

This guide covers security best practices for deploying Tempo in production.

## Critical Security Requirements

### JWT Secret Key

**MUST** be configured in production. The default placeholder value will cause startup failure.

- Generate a secure random key: `openssl rand -base64 32`
- Minimum 32 characters
- Store securely (environment variables, secrets manager)
- Never commit to version control
- Rotate periodically

### HTTPS

**REQUIRED** for production deployments. JWT tokens are stored in httpOnly cookies, which require HTTPS for secure transmission.

- Use valid SSL/TLS certificates
- Configure reverse proxy (Nginx, Traefik, etc.)
- Redirect HTTP to HTTPS
- Use strong cipher suites

### Database Security

- Change default PostgreSQL password
- Use strong passwords (minimum 16 characters)
- Restrict database access to application only
- Use connection encryption if accessing remotely
- Regular security updates

## Authentication Security

### Password Requirements

- Enforce strong passwords (implement in frontend if needed)
- Use BCrypt for password hashing (already implemented)
- Consider password complexity requirements

### JWT Configuration

- Set appropriate expiration (default: 7 days)
- Use secure random secret key
- Configure issuer and audience
- Store tokens in httpOnly cookies (already implemented)

### Registration Lock

- Registration is automatically locked after first user
- Prevents unauthorized account creation
- Single-user deployment pattern

## Network Security

### Firewall Configuration

- Only expose necessary ports
- Use firewall rules to restrict access
- Consider VPN for administrative access
- Block unnecessary network traffic

### CORS Configuration

Configure allowed origins appropriately:

```yaml
CORS__AllowedOrigins: "https://yourdomain.com"
```

- Only include trusted domains
- Don't use wildcards in production
- Test CORS configuration thoroughly

### Reverse Proxy

Use a reverse proxy (Nginx, Traefik) to:
- Terminate SSL/TLS
- Hide internal service ports
- Add security headers
- Implement rate limiting

## Application Security

### Input Validation

- All file uploads are validated (size, MIME type)
- File paths are sanitized
- SQL injection prevented by Entity Framework parameterization
- XSS protection via React's built-in escaping

### File Upload Security

- Maximum file size limits (50MB default, 500MB for bulk import)
- MIME type validation
- File extension validation
- Storage outside web root

### Error Handling

- Don't expose sensitive information in error messages
- Log errors securely
- Use generic error messages for users
- Detailed errors only in development mode

## Data Security

### Data Encryption

- Use HTTPS for data in transit
- Consider database encryption at rest
- Encrypt backups
- Secure media file storage

### Backup Security

- Encrypt backup files
- Store backups securely
- Limit backup access
- Test restore procedures

### Data Privacy

- All data stored locally (no cloud sync)
- User controls all data
- No third-party data sharing
- GDPR considerations for EU users

## Container Security

### Image Security

- Use official base images
- Keep images updated
- Scan images for vulnerabilities
- Use specific version tags (not `latest`)

### Container Configuration

- Run containers as non-root user (if possible)
- Limit container capabilities
- Use read-only filesystems where possible
- Implement resource limits

### Secrets Management

- Use Docker secrets or environment variables
- Never hardcode secrets
- Rotate secrets regularly
- Use secrets management tools (HashiCorp Vault, etc.)

## Monitoring and Auditing

### Logging

- Log authentication events
- Log security-relevant actions
- Monitor for suspicious activity
- Retain logs appropriately

### Monitoring

- Monitor failed login attempts
- Track unusual API usage
- Monitor resource usage
- Set up alerts for anomalies

### Regular Updates

- Keep dependencies updated
- Apply security patches promptly
- Monitor security advisories
- Test updates in staging first

## Security Headers

Configure security headers in reverse proxy:

```nginx
add_header X-Frame-Options "SAMEORIGIN" always;
add_header X-Content-Type-Options "nosniff" always;
add_header X-XSS-Protection "1; mode=block" always;
add_header Referrer-Policy "strict-origin-when-cross-origin" always;
add_header Content-Security-Policy "default-src 'self'" always;
```

## Checklist

### Pre-Deployment

- [ ] JWT secret key configured and secure
- [ ] Database password changed
- [ ] HTTPS configured
- [ ] CORS origins configured
- [ ] Firewall rules set
- [ ] Reverse proxy configured
- [ ] Security headers added

### Ongoing

- [ ] Regular security updates
- [ ] Monitor logs for suspicious activity
- [ ] Review access logs
- [ ] Test backup and restore
- [ ] Rotate secrets periodically
- [ ] Review and update dependencies

## Incident Response

If a security issue is discovered:

1. Assess the severity
2. Contain the issue
3. Notify affected users if necessary
4. Apply fixes
5. Review and improve security measures
6. Document the incident

## Reporting Security Issues

Report security vulnerabilities responsibly:

- Email: [security contact if available]
- GitHub: Create a private security advisory
- Do not disclose publicly until fixed

## Next Steps

- [Review production setup](production.md)
- [Set up monitoring](production.md#monitoring-and-logging)
- [Configure backups](backup-restore.md)

