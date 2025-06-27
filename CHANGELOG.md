# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.0] - 2025-06-27

### Changed
- **Performance Optimization**: Reduced continuous transmission interval from 750ms to 500ms
- Updated all timing references and status messages to reflect 500ms intervals
- Enhanced transmission performance for faster robot response cycles
- Improved real-time communication responsiveness

### Updated
- All debug messages and status displays now show correct 500ms timing
- UI status messages updated to show "10 packets every 500ms"
- System performance validated at new 500ms interval with excellent reliability

## [1.4.0] - 2025-06-26

### Added
- Master-slave communication architecture with clear role identification
- Force disconnect method to prevent hanging during Bluetooth disconnection
- Enhanced timeout handling for all Bluetooth operations
- Comprehensive error handling with individual try-catch blocks for each operation

### Changed
- Improved `DisconnectAsync()` method with timeout protection (2-8 seconds max)
- Enhanced `StopTransmissionSystem()` with robust error handling
- Updated status messages to clearly indicate master/slave roles
- Regular mode now handles 82-byte slave responses automatically

### Fixed
- Resolved Bluetooth disconnection hanging issues
- Fixed potential deadlocks during application shutdown
- Improved connection health monitoring

## [1.3.0] - 2025-06-25

### Added
- 82-byte response parsing for both continuous and regular modes
- Automatic parsing of robot responses into 10×8-byte packets
- Clear UI display of parsed packet data with indexing
- Enhanced continuous mode response collection with buffer management

### Changed
- Updated `ProcessNormalModeData()` to handle 82-byte blocks in all modes
- Improved `CollectContinuousResponses()` with better data accumulation
- Enhanced status messages with block markers and packet numbering

### Fixed
- Proper handling of partial 82-byte responses
- Correct buffer management during data collection

## [1.2.0] - 2025-06-24

### Added
- Continuous transmission mode (marker + 10 packets every 200ms)
- 82-byte command format with 2-byte marker and 80 bytes of data
- Listen-only mode for pure diagnostic monitoring
- Queue-based data management system
- Connection health monitoring with automatic detection

### Changed
- Optimized all timing for 115200 baud rate (12x faster than 9600)
- Reduced transmission intervals and timeouts for high-speed operation
- Enhanced data reading with better buffer management

### Fixed
- Improved reliability at high data rates
- Better error handling for connection issues

## [1.1.0] - 2025-06-23

### Added
- WebSocket server integration for Scratch extension communication
- Support for binary data transmission from WebSocket clients
- Real-time data forwarding from WebSocket to Bluetooth
- Queue status reporting and transmission status events

### Changed
- Enhanced UI with WebSocket server controls
- Improved data packet handling with validation
- Added comprehensive status reporting

### Fixed
- Thread safety issues with UI updates
- WebSocket connection stability

## [1.0.0] - 2025-06-22

### Added
- Initial release with basic Bluetooth HC-05 communication
- Device scanning and connection management
- 8-byte data packet transmission
- Windows Forms UI with device selection
- Basic error handling and status reporting
- Support for multiple Bluetooth service UUIDs
- Connection timeout and retry logic

### Technical Specifications
- Target Framework: .NET Framework 4.8+
- Bluetooth Library: 32feet.NET
- Default Baud Rate: 115200 (optimized for HC-05)
- Data Format: 8-byte packets
- UI Framework: Windows Forms

## [Unreleased]

### Planned Features
- Bluetooth Low Energy (BLE) support
- Multiple device connections
- Data logging and replay functionality
- Configuration file support
- Advanced diagnostics and performance monitoring

---

## Version History Summary

- **v1.4**: Enhanced disconnect handling and master-slave architecture
- **v1.3**: 82-byte response parsing and display improvements
- **v1.2**: Continuous mode and high-speed optimization
- **v1.1**: WebSocket integration for Scratch extensions
- **v1.0**: Initial Bluetooth communication foundation
