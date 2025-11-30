# Contributing Guide

Thank you for your interest in contributing to Tempo! This guide will help you get started.

## Getting Started

1. Fork the repository
2. Clone your fork
3. Set up your development environment (see [Development Setup](setup.md))
4. Create a feature branch
5. Make your changes
6. Test thoroughly
7. Submit a pull request

## Development Workflow

### 1. Create a Feature Branch

```bash
git checkout -b feature/your-feature-name
```

Use descriptive branch names:
- `feature/add-workout-export`
- `fix/heart-rate-zone-calculation`
- `docs/update-api-documentation`

### 2. Make Your Changes

- Write clean, maintainable code
- Follow existing code style and patterns
- Add comments for complex logic
- Update documentation as needed

### 3. Test Your Changes

- Test locally with sample data
- Verify API endpoints work correctly
- Test frontend changes in different browsers
- Check for console errors and warnings

### 4. Commit Your Changes

Write clear, descriptive commit messages:

```bash
git commit -m "Add workout export functionality

- Implement GPX export endpoint
- Add export button to workout details page
- Include route and metadata in export"
```

### 5. Push and Create Pull Request

```bash
git push origin feature/your-feature-name
```

Then create a pull request on GitHub.

## Code Style and Conventions

### Backend (C#)

- Follow C# naming conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and single-purpose
- Use dependency injection for services

### Frontend (TypeScript/React)

- Use TypeScript for type safety
- Follow React best practices
- Use functional components with hooks
- Keep components small and focused
- Use Tailwind CSS for styling

### General

- Write self-documenting code
- Add comments for complex logic
- Keep functions and methods focused
- Avoid code duplication

## Pull Request Guidelines

### Before Submitting

- [ ] Code follows project style guidelines
- [ ] Changes are tested locally
- [ ] Documentation is updated if needed
- [ ] No console errors or warnings
- [ ] Commit messages are clear and descriptive

### Pull Request Description

Include:
- Description of changes
- Why the changes are needed
- How to test the changes
- Screenshots (for UI changes)
- Related issues (if any)

### Review Process

- All pull requests require review
- Address review comments promptly
- Be open to feedback and suggestions
- Keep discussions constructive

## Areas for Contribution

### Features

- New workout import formats
- Additional analytics and statistics
- Export functionality
- Mobile app support
- Integration with other services

### Improvements

- Performance optimizations
- UI/UX enhancements
- Documentation improvements
- Test coverage
- Bug fixes

### Documentation

- User guide improvements
- API documentation
- Code comments
- Architecture diagrams

## Testing

### Manual Testing

- Test with sample workout files
- Verify all features work as expected
- Test edge cases and error handling
- Check browser compatibility

### API Testing

Use the Bruno API testing collection in `api/bruno/Tempo.Api/` to test API changes.

## Reporting Issues

### Bug Reports

Include:
- Description of the bug
- Steps to reproduce
- Expected behavior
- Actual behavior
- Environment details (OS, browser, versions)
- Screenshots if applicable

### Feature Requests

Include:
- Description of the feature
- Use case and motivation
- Proposed implementation (if you have ideas)
- Alternatives considered

## Code of Conduct

- Be respectful and inclusive
- Welcome newcomers and help them learn
- Focus on constructive feedback
- Be open to different perspectives

## Questions?

- Open an issue for questions or discussions
- Join the [Discord community](https://discord.gg/9Svd99npyj)
- Check existing issues and pull requests

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

Thank you for contributing to Tempo!

