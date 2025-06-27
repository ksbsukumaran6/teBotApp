# Contributing to TeBot

Thank you for your interest in contributing to TeBot! This document provides guidelines and information for contributors.

## Table of Contents
- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [How to Contribute](#how-to-contribute)
- [Coding Standards](#coding-standards)
- [Testing](#testing)
- [Pull Request Process](#pull-request-process)

## Code of Conduct

By participating in this project, you agree to abide by our code of conduct:
- Be respectful and inclusive
- Focus on constructive feedback
- Help others learn and grow
- Maintain a professional tone in all interactions

## Getting Started

### Prerequisites
- Windows 10/11 with Bluetooth support
- Visual Studio 2019+ or Visual Studio Code  
- .NET Framework 4.8 or later
- Git for version control
- HC-05 Bluetooth module for testing (recommended)
- External USB Bluetooth dongle (TP-Link recommended for best performance)

### Development Setup

1. **Fork the repository**
   ```bash
   git clone https://github.com/your-username/TeBot.git
   cd TeBot
   ```

2. **Open the solution**
   - Open `TeBot.sln` in Visual Studio
   - Or use Visual Studio Code with C# extension

3. **Restore packages**
   ```bash
   dotnet restore
   ```

4. **Build the project**
   ```bash
   dotnet build
   ```

## How to Contribute

### Reporting Issues
- Use the GitHub issue tracker
- Provide detailed reproduction steps
- Include system information (OS, .NET version, Bluetooth hardware)
- Attach logs or screenshots when relevant

### Suggesting Features
- Open an issue with the "enhancement" label
- Clearly describe the feature and its benefits
- Consider implementation complexity and backward compatibility

### Contributing Code
1. Create a feature branch: `git checkout -b feature/amazing-feature`
2. Make your changes
3. Test thoroughly
4. Commit with clear messages
5. Push to your fork
6. Create a Pull Request

## Coding Standards

### C# Style Guidelines
- Follow Microsoft's C# coding conventions
- Use PascalCase for public members
- Use camelCase for private fields and local variables
- Use meaningful names for variables and methods

### Example:
```csharp
public class BluetoothManager
{
    private bool _isConnected;
    private readonly object _transmissionLock;
    
    public async Task<bool> ConnectToDeviceAsync(BluetoothDeviceInfo device)
    {
        // Implementation here
    }
    
    private void HandleConnectionError(Exception ex)
    {
        // Error handling
    }
}
```

### Documentation
- Use XML documentation comments for public APIs
- Include parameter descriptions and return values
- Add inline comments for complex logic

```csharp
/// <summary>
/// Sends data to the connected Bluetooth device
/// </summary>
/// <param name="data">8-byte data packet to send</param>
/// <returns>True if data was sent successfully, false otherwise</returns>
public async Task<bool> SendDataAsync(byte[] data)
{
    // Validate data size
    if (data == null || data.Length != DATA_PACKET_SIZE)
    {
        return false;
    }
    
    // Implementation...
}
```

### Error Handling
- Always handle exceptions appropriately
- Log errors with sufficient detail
- Provide user-friendly error messages
- Use specific exception types when possible

## Testing

### Manual Testing
- Test with real HC-05 hardware when possible
- Verify all UI interactions work correctly
- Test connection/disconnection scenarios
- Validate data transmission accuracy

### Test Cases to Cover
1. **Bluetooth Connection**
   - Device scanning and discovery
   - Connection establishment and failure scenarios
   - Disconnection handling
   - Multiple connection attempts

2. **Data Transmission**
   - Single packet transmission
   - Continuous mode operation
   - WebSocket data forwarding
   - Error recovery

3. **UI Functionality**
   - Button state management
   - Status message display
   - Thread safety for UI updates
   - Window closing behavior

### Performance Testing
- Test high-frequency data transmission at 500ms intervals
- Monitor memory usage during long sessions
- Verify no memory leaks in continuous mode
- Check response times at 115200 baud
- Validate stable operation with external Bluetooth dongles
- Test master-slave communication reliability over extended periods

## Pull Request Process

### Before Submitting
1. **Test thoroughly**
   - All existing functionality still works
   - New features work as expected
   - No new crashes or hangs

2. **Update documentation**
   - Update README.md if needed
   - Add/update code comments
   - Update CHANGELOG.md

3. **Code review checklist**
   - Code follows style guidelines
   - No hardcoded values (use constants)
   - Proper error handling
   - Thread-safe operations

### Pull Request Description
Include in your PR description:
- **What**: Brief description of changes
- **Why**: Reason for the changes
- **How**: Technical approach used
- **Testing**: How you tested the changes
- **Breaking Changes**: Any backward compatibility issues

### Example PR Template
```markdown
## Description
Brief description of the changes made.

## Motivation and Context
Why is this change required? What problem does it solve?

## How Has This Been Tested?
- [ ] Manual testing with HC-05 hardware
- [ ] UI functionality verified
- [ ] Continuous mode tested
- [ ] WebSocket integration tested

## Types of Changes
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)

## Checklist
- [ ] My code follows the code style of this project
- [ ] I have updated the documentation accordingly
- [ ] I have added tests to cover my changes
- [ ] All new and existing tests passed
```

## Development Tips

### Debugging Bluetooth Issues
- Use Windows Event Viewer for Bluetooth system events
- Enable debug output in the application
- Test with multiple HC-05 modules if available
- Use a Bluetooth protocol analyzer if needed

### Performance Optimization
- Profile memory usage during long runs
- Monitor CPU usage in continuous mode
- Optimize UI update frequency
- Use async/await properly to avoid blocking

### Common Pitfalls
- **UI Thread Safety**: Always use `Invoke()` for UI updates from background threads
- **Resource Cleanup**: Properly dispose of Bluetooth resources
- **Timeout Handling**: Don't rely on indefinite waits
- **Exception Handling**: Catch specific exceptions when possible

## Getting Help

- **Documentation**: Check PROJECT_DOCUMENTATION.md and CODE_ANALYSIS.md
- **Issues**: Search existing issues before creating new ones
- **Discussions**: Use GitHub Discussions for questions
- **Code Review**: Ask for feedback early and often

## Recognition

Contributors will be recognized in:
- CHANGELOG.md for significant contributions
- README.md acknowledgments section
- GitHub contributors list

Thank you for contributing to TeBot! 🤖📡
