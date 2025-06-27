class Scratch3TeBotBlocks {
    constructor(runtime) {
        this.runtime = runtime;
        this.socket = null;
        
        // Command buffer system for high-frequency operations
        this.commandBuffer = {
            movement: { command: 0x00, value: 0, timestamp: 0 },
            led: { matrix: '00000:00000:00000:00000:00000', brightness: 100, timestamp: 0 },
            sound: { frequency: 0, duration: 0, timestamp: 0 },
            sensors: { 
                requests: new Set(), // Track which sensors to request
                timestamp: 0 
            }
        };
        
        // Sensor data cache with timestamps
        this.sensorCache = {
            ir: { value: 0, timestamp: 0, maxAge: 100 },
            ultrasonic: { value: 0, timestamp: 0, maxAge: 100 },
            button: { value: false, timestamp: 0, maxAge: 100 },
            light: { value: 0, timestamp: 0, maxAge: 100 },
            temperature: { value: 0, timestamp: 0, maxAge: 100 },
            acceleration: { x: 0, y: 0, z: 0, timestamp: 0, maxAge: 100 },
            battery: { value: 100, timestamp: 0, maxAge: 1000 } // Battery updates less frequently
        };
        
        // Event flags for hat blocks
        this.eventFlags = {
            dataReceived: false,
            sensorChanged: {
                ir: false,
                ultrasonic: false,
                button: false,
                light: false,
                temperature: false,
                acceleration: false
            },
            buttonPressed: false
        };
        
        // Semaphore system for thread safety
        this.semaphore = {
            sending: false,
            bufferLock: false,
            maxWaitTime: 50,
            lockTimeout: null
        };
        
        // Communication settings
        this.bufferSendInterval = 150; // Send every 150ms
        this.minSendInterval = 50;     // Minimum time between sends
        this.maxQueueSize = 20;        // Maximum queued messages
        this.lastSendTime = 0;
        this.requestId = 0;
        this.sendTimer = null;
        
        // Auto-start buffer transmission when connection opens
        this.autoStartBuffer = true;
    }

    getInfo() {
        return {
            id: 'tebot',
            name: 'TeBot Robot',
            color1: '#4C97FF',
            color2: '#4280D7',
            blockIconURI: 'data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNDAiIGhlaWdodD0iNDAiIHZpZXdCb3g9IjAgMCA0MCA0MCIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHJlY3Qgd2lkdGg9IjQwIiBoZWlnaHQ9IjQwIiBmaWxsPSIjNEM5N0ZGIiByeD0iNCIvPgo8dGV4dCB4PSI1IiB5PSIyNSIgZm9udC1zaXplPSIxNCIgZmlsbD0id2hpdGUiIGZvbnQtZmFtaWx5PSJBcmlhbCI+VEI8L3RleHQ+Cjwvc3ZnPg==',
            blocks: [
                // Connection Management
                {
                    opcode: 'openConnection',
                    blockType: 'command',
                    text: 'connect to TeBot server'
                },
                {
                    opcode: 'closeConnection',
                    blockType: 'command',
                    text: 'disconnect from TeBot server'
                },
                {
                    opcode: 'isConnected',
                    blockType: 'Boolean',
                    text: 'connected to TeBot?'
                },

                '---',

                // Hat Blocks (Event-driven)
                {
                    opcode: 'whenDataReceived',
                    blockType: 'hat',
                    text: 'when data received from TeBot'
                },
                {
                    opcode: 'whenSensorChanged',
                    blockType: 'hat',
                    text: 'when [SENSOR] sensor changes',
                    arguments: {
                        SENSOR: {
                            type: 'string',
                            menu: 'sensorTypes',
                            defaultValue: 'ir'
                        }
                    }
                },
                {
                    opcode: 'whenButtonPressed',
                    blockType: 'hat',
                    text: 'when TeBot button pressed'
                },

                '---',

                // Movement Commands
                {
                    opcode: 'moveForward',
                    blockType: 'command',
                    text: 'move forward [STEPS] steps',
                    arguments: {
                        STEPS: {
                            type: 'number',
                            defaultValue: 10
                        }
                    }
                },
                {
                    opcode: 'moveBackward',
                    blockType: 'command',
                    text: 'move backward [STEPS] steps',
                    arguments: {
                        STEPS: {
                            type: 'number',
                            defaultValue: 10
                        }
                    }
                },
                {
                    opcode: 'turnLeft',
                    blockType: 'command',
                    text: 'turn left [DEGREES] degrees',
                    arguments: {
                        DEGREES: {
                            type: 'number',
                            defaultValue: 90
                        }
                    }
                },
                {
                    opcode: 'turnRight',
                    blockType: 'command',
                    text: 'turn right [DEGREES] degrees',
                    arguments: {
                        DEGREES: {
                            type: 'number',
                            defaultValue: 90
                        }
                    }
                },
                {
                    opcode: 'stop',
                    blockType: 'command',
                    text: 'stop robot'
                },
                {
                    opcode: 'setSpeed',
                    blockType: 'command',
                    text: 'set speed to [SPEED] %',
                    arguments: {
                        SPEED: {
                            type: 'number',
                            defaultValue: 50
                        }
                    }
                },

                '---',

                // Display & LEDs
                {
                    opcode: 'displayLED',
                    blockType: 'command',
                    text: 'display LED pattern [PATTERN]',
                    arguments: {
                        PATTERN: {
                            type: 'string',
                            defaultValue: '11111:10001:10101:10001:11111'
                        }
                    }
                },
                {
                    opcode: 'showText',
                    blockType: 'command',
                    text: 'show text [TEXT]',
                    arguments: {
                        TEXT: {
                            type: 'string',
                            defaultValue: 'Hello'
                        }
                    }
                },
                {
                    opcode: 'setLEDBrightness',
                    blockType: 'command',
                    text: 'set LED brightness to [BRIGHTNESS] %',
                    arguments: {
                        BRIGHTNESS: {
                            type: 'number',
                            defaultValue: 100
                        }
                    }
                },

                '---',

                // Sound
                {
                    opcode: 'playTone',
                    blockType: 'command',
                    text: 'play tone [FREQUENCY] Hz for [DURATION] ms',
                    arguments: {
                        FREQUENCY: {
                            type: 'number',
                            defaultValue: 440
                        },
                        DURATION: {
                            type: 'number',
                            defaultValue: 500
                        }
                    }
                },
                {
                    opcode: 'playNote',
                    blockType: 'command',
                    text: 'play note [NOTE] for [DURATION] ms',
                    arguments: {
                        NOTE: {
                            type: 'string',
                            menu: 'notes',
                            defaultValue: 'C4'
                        },
                        DURATION: {
                            type: 'number',
                            defaultValue: 500
                        }
                    }
                },

                '---',

                // Sensors
                {
                    opcode: 'senseIR',
                    blockType: 'reporter',
                    text: 'IR sensor value'
                },
                {
                    opcode: 'senseUltrasonic',
                    blockType: 'reporter',
                    text: 'ultrasonic distance'
                },
                {
                    opcode: 'senseButton',
                    blockType: 'Boolean',
                    text: 'button pressed?'
                },
                {
                    opcode: 'senseLight',
                    blockType: 'reporter',
                    text: 'light sensor value'
                },
                {
                    opcode: 'senseTemperature',
                    blockType: 'reporter',
                    text: 'temperature °C'
                },
                {
                    opcode: 'getAcceleration',
                    blockType: 'reporter',
                    text: 'acceleration [AXIS]',
                    arguments: {
                        AXIS: {
                            type: 'string',
                            menu: 'axes',
                            defaultValue: 'x'
                        }
                    }
                },
                {
                    opcode: 'getBatteryLevel',
                    blockType: 'reporter',
                    text: 'battery level %'
                },
                {
                    opcode: 'isObstacleDetected',
                    blockType: 'Boolean',
                    text: 'obstacle detected within [DISTANCE] cm?',
                    arguments: {
                        DISTANCE: {
                            type: 'number',
                            defaultValue: 10
                        }
                    }
                },

                '---',

                // Utility
                {
                    opcode: 'calibrateSensors',
                    blockType: 'command',
                    text: 'calibrate sensors'
                },
                {
                    opcode: 'resetPosition',
                    blockType: 'command',
                    text: 'reset robot position'
                }
            ],
            menus: {
                sensorTypes: {
                    acceptReporters: true,
                    items: [
                        { text: 'IR sensor', value: 'ir' },
                        { text: 'ultrasonic', value: 'ultrasonic' },
                        { text: 'button', value: 'button' },
                        { text: 'light sensor', value: 'light' },
                        { text: 'temperature', value: 'temperature' },
                        { text: 'acceleration', value: 'acceleration' }
                    ]
                },
                axes: {
                    acceptReporters: true,
                    items: [
                        { text: 'X', value: 'x' },
                        { text: 'Y', value: 'y' },
                        { text: 'Z', value: 'z' }
                    ]
                },
                notes: {
                    acceptReporters: true,
                    items: [
                        { text: 'C4', value: 'C4' },
                        { text: 'D4', value: 'D4' },
                        { text: 'E4', value: 'E4' },
                        { text: 'F4', value: 'F4' },
                        { text: 'G4', value: 'G4' },
                        { text: 'A4', value: 'A4' },
                        { text: 'B4', value: 'B4' },
                        { text: 'C5', value: 'C5' }
                    ]
                }
            }
        };
    }

    // ===== SEMAPHORE SYSTEM =====

    acquireBufferLock(callback) {
        const tryAcquire = () => {
            if (!this.semaphore.bufferLock) {
                this.semaphore.bufferLock = true;
                
                // Auto-release timeout
                this.semaphore.lockTimeout = setTimeout(() => {
                    if (this.semaphore.bufferLock) {
                        console.warn('Buffer lock auto-released due to timeout');
                        this.releaseBufferLock();
                    }
                }, this.semaphore.maxWaitTime);
                
                callback();
                return true;
            }
            return false;
        };

        if (!tryAcquire()) {
            // Try again with small delay
            const startTime = Date.now();
            const retryInterval = setInterval(() => {
                if (tryAcquire()) {
                    clearInterval(retryInterval);
                } else if (Date.now() - startTime > this.semaphore.maxWaitTime) {
                    clearInterval(retryInterval);
                    console.warn('Buffer lock acquisition timeout');
                }
            }, 1);
        }
    }

    releaseBufferLock() {
        this.semaphore.bufferLock = false;
        if (this.semaphore.lockTimeout) {
            clearTimeout(this.semaphore.lockTimeout);
            this.semaphore.lockTimeout = null;
        }
    }

    acquireSendLock() {
        return new Promise((resolve, reject) => {
            const tryAcquire = () => {
                if (!this.semaphore.sending) {
                    this.semaphore.sending = true;
                    resolve();
                    return true;
                }
                return false;
            };

            if (!tryAcquire()) {
                const startTime = Date.now();
                const retryInterval = setInterval(() => {
                    if (tryAcquire()) {
                        clearInterval(retryInterval);
                    } else if (Date.now() - startTime > this.semaphore.maxWaitTime) {
                        clearInterval(retryInterval);
                        reject(new Error('Send lock acquisition timeout'));
                    }
                }, 1);
            }
        });
    }

    releaseSendLock() {
        this.semaphore.sending = false;
    }

    // ===== CONNECTION MANAGEMENT =====

    openConnection() {
        console.log('Opening WebSocket connection to TeBot server...');
        
        if (this.socket && this.socket.readyState === WebSocket.OPEN) {
            console.log('WebSocket is already open');
            return;
        }

        // Clear any existing socket
        if (this.socket) {
            this.socket.close();
        }

        this.socket = new WebSocket('ws://localhost:5000');
        this.socket.binaryType = 'arraybuffer'; // Optimize for binary data

        this.socket.onopen = () => {
            console.log('✅ Connected to TeBot server');
            this.resetBufferSystem();
            
            if (this.autoStartBuffer) {
                this.startBufferTransmission();
            }
        };

        this.socket.onmessage = (event) => {
            this.processReceivedData(event.data);
        };

        this.socket.onclose = (event) => {
            console.log(`❌ TeBot connection closed: ${event.code} - ${event.reason}`);
            this.stopBufferTransmission();
            
            // Auto-reconnect if not intentionally closed
            if (event.code !== 1000) {
                setTimeout(() => this.openConnection(), 2000);
            }
        };

        this.socket.onerror = (error) => {
            console.error('❌ TeBot WebSocket error:', error);
        };
    }

    closeConnection() {
        console.log('Closing TeBot connection...');
        this.stopBufferTransmission();
        
        if (this.socket) {
            this.socket.close(1000, 'User requested disconnect');
            this.socket = null;
        }
    }

    isConnected() {
        return this.socket && this.socket.readyState === WebSocket.OPEN;
    }

    // ===== BUFFER MANAGEMENT =====

    resetBufferSystem() {
        this.acquireBufferLock(() => {
            // Reset all buffers
            this.commandBuffer.movement = { command: 0x00, value: 0, timestamp: 0 };
            this.commandBuffer.led = { matrix: '00000:00000:00000:00000:00000', brightness: 100, timestamp: 0 };
            this.commandBuffer.sound = { frequency: 0, duration: 0, timestamp: 0 };
            this.commandBuffer.sensors.requests.clear();
            this.commandBuffer.sensors.timestamp = 0;
            
            // Reset event flags
            this.eventFlags.dataReceived = false;
            this.eventFlags.buttonPressed = false;
            Object.keys(this.eventFlags.sensorChanged).forEach(sensor => {
                this.eventFlags.sensorChanged[sensor] = false;
            });
            
            console.log('📋 Buffer system reset');
        });
    }

    startBufferTransmission() {
        if (this.sendTimer) {
            this.stopBufferTransmission();
        }
        
        console.log(`🔄 Starting buffer transmission every ${this.bufferSendInterval}ms`);
        this.sendTimer = setInterval(() => {
            this.sendBufferData();
        }, this.bufferSendInterval);
    }

    stopBufferTransmission() {
        if (this.sendTimer) {
            clearInterval(this.sendTimer);
            this.sendTimer = null;
            console.log('⏹️ Buffer transmission stopped');
        }
    }

    sendBufferData() {
        if (!this.isConnected()) {
            return;
        }

        this.acquireSendLock()
            .then(() => {
                const packet = this.createBufferPacket();
                if (packet) {
                    this.socket.send(packet);
                    this.lastSendTime = Date.now();
                    console.log('📤 Sent buffer packet to TeBot server');
                }
            })
            .catch((error) => {
                console.warn('⚠️ Skipped send due to lock timeout:', error.message);
            })
            .finally(() => {
                this.releaseSendLock();
            });
    }

    createBufferPacket() {
        let hasRecentData = false;
        const packet = new Uint8Array(80);
        const now = Date.now();
        let dataOffset = 8; // Start after header
        
        // Header
        packet[0] = 0xAA;  // Start marker 1
        packet[1] = 0x55;  // Start marker 2
        
        // Request ID
        const requestId = ++this.requestId;
        packet[2] = requestId & 0xFF;
        packet[3] = (requestId >> 8) & 0xFF;
        
        // Timestamp
        const timestamp = now & 0xFFFF;
        packet[5] = timestamp & 0xFF;
        packet[6] = (timestamp >> 8) & 0xFF;
        
        this.acquireBufferLock(() => {
            // Movement commands (valid for 500ms)
            if (this.commandBuffer.movement.timestamp > 0 && 
                (now - this.commandBuffer.movement.timestamp) < 500) {
                
                if (dataOffset + 2 <= 72) {
                    packet[dataOffset] = this.commandBuffer.movement.command;
                    packet[dataOffset + 1] = this.commandBuffer.movement.value;
                    dataOffset += 2;
                    hasRecentData = true;
                }
                
                // Clear after sending
                this.commandBuffer.movement.timestamp = 0;
            }
            
            // LED matrix commands (valid for 1000ms)
            if (this.commandBuffer.led.timestamp > 0 && 
                (now - this.commandBuffer.led.timestamp) < 1000) {
                
                if (dataOffset + 7 <= 72) {
                    packet[dataOffset] = 0x07; // CMD_SET_LED
                    packet[dataOffset + 1] = this.commandBuffer.led.brightness;
                    
                    // Convert LED matrix to bytes (simplified encoding)
                    const matrixBytes = this.encodeLEDMatrix(this.commandBuffer.led.matrix);
                    matrixBytes.forEach((byte, i) => {
                        if (i < 5 && dataOffset + 2 + i < 72) {
                            packet[dataOffset + 2 + i] = byte;
                        }
                    });
                    
                    dataOffset += 7;
                    hasRecentData = true;
                }
                
                // Clear after sending
                this.commandBuffer.led.timestamp = 0;
            }
            
            // Sound commands (valid for 200ms)
            if (this.commandBuffer.sound.timestamp > 0 && 
                (now - this.commandBuffer.sound.timestamp) < 200) {
                
                if (dataOffset + 5 <= 72) {
                    packet[dataOffset] = 0x08; // CMD_PLAY_SOUND
                    packet[dataOffset + 1] = this.commandBuffer.sound.frequency & 0xFF;
                    packet[dataOffset + 2] = (this.commandBuffer.sound.frequency >> 8) & 0xFF;
                    packet[dataOffset + 3] = this.commandBuffer.sound.duration & 0xFF;
                    packet[dataOffset + 4] = (this.commandBuffer.sound.duration >> 8) & 0xFF;
                    dataOffset += 5;
                    hasRecentData = true;
                }
                
                // Clear after sending
                this.commandBuffer.sound.timestamp = 0;
            }
            
            // Sensor requests (send once and clear)
            if (this.commandBuffer.sensors.requests.size > 0) {
                this.commandBuffer.sensors.requests.forEach(request => {
                    if (dataOffset + 2 <= 72) {
                        packet[dataOffset] = request.command;     // CMD_READ_SENSOR (0x06)
                        packet[dataOffset + 1] = request.sensor; // Sensor type parameter
                        dataOffset += 2;
                        hasRecentData = true;
                    }
                });
                
                // Clear requests after sending
                this.commandBuffer.sensors.requests.clear();
            }
        });
        
        // Data length
        packet[4] = dataOffset - 8;
        
        // Calculate checksum
        let checksum = 0;
        for (let i = 0; i < 72; i++) {
            checksum ^= packet[i];
        }
        packet[72] = checksum;
        
        // Return packet only if there's recent data
        return hasRecentData ? packet : null;
    }

    // ===== MOVEMENT COMMANDS =====

    moveForward(args) {
        const steps = Math.max(0, Math.min(255, args.STEPS || 10));
        this.updateMovementBuffer(0x01, steps);
        console.log(`📱 Move forward ${steps} steps queued`);
    }

    moveBackward(args) {
        const steps = Math.max(0, Math.min(255, args.STEPS || 10));
        this.updateMovementBuffer(0x02, steps);
        console.log(`📱 Move backward ${steps} steps queued`);
    }

    turnLeft(args) {
        const degrees = Math.max(0, Math.min(360, args.DEGREES || 90));
        this.updateMovementBuffer(0x03, degrees);
        console.log(`📱 Turn left ${degrees}° queued`);
    }

    turnRight(args) {
        const degrees = Math.max(0, Math.min(360, args.DEGREES || 90));
        this.updateMovementBuffer(0x04, degrees);
        console.log(`📱 Turn right ${degrees}° queued`);
    }

    stop() {
        this.updateMovementBuffer(0x00, 0);
        console.log('📱 Stop command queued');
    }

    setSpeed(args) {
        const speed = Math.max(0, Math.min(100, args.SPEED || 50));
        this.updateMovementBuffer(0x05, speed);  // Fixed: Use 0x05 to match Arduino CMD_SET_SPEED
        console.log(`📱 Set speed ${speed}% queued`);
    }

    updateMovementBuffer(command, value) {
        this.acquireBufferLock(() => {
            this.commandBuffer.movement.command = command;
            this.commandBuffer.movement.value = value;
            this.commandBuffer.movement.timestamp = Date.now();
        });
    }

    // ===== DISPLAY COMMANDS =====

    displayLED(args) {
        const pattern = args.PATTERN || '00000:00000:00000:00000:00000';
        this.updateLEDBuffer(pattern, this.commandBuffer.led.brightness);
        console.log(`📱 LED pattern queued: ${pattern}`);
    }

    showText(args) {
        const text = args.TEXT || '';
        // Convert text to LED pattern (simplified)
        const pattern = this.textToLEDPattern(text);
        this.updateLEDBuffer(pattern, this.commandBuffer.led.brightness);
        console.log(`📱 Text "${text}" converted to LED pattern`);
    }

    setLEDBrightness(args) {
        const brightness = Math.max(0, Math.min(100, args.BRIGHTNESS || 100));
        this.updateLEDBuffer(this.commandBuffer.led.matrix, brightness);
        console.log(`📱 LED brightness ${brightness}% queued`);
    }

    updateLEDBuffer(matrix, brightness) {
        this.acquireBufferLock(() => {
            this.commandBuffer.led.matrix = matrix;
            this.commandBuffer.led.brightness = brightness;
            this.commandBuffer.led.timestamp = Date.now();
        });
    }

    // ===== SOUND COMMANDS =====

    playTone(args) {
        const frequency = Math.max(50, Math.min(5000, args.FREQUENCY || 440));
        const duration = Math.max(50, Math.min(5000, args.DURATION || 500));
        this.updateSoundBuffer(frequency, duration);
        console.log(`📱 Play tone ${frequency}Hz for ${duration}ms queued`);
    }

    playNote(args) {
        const note = args.NOTE || 'C4';
        const duration = Math.max(50, Math.min(5000, args.DURATION || 500));
        const frequency = this.noteToFrequency(note);
        this.updateSoundBuffer(frequency, duration);
        console.log(`📱 Play note ${note} (${frequency}Hz) for ${duration}ms queued`);
    }

    updateSoundBuffer(frequency, duration) {
        this.acquireBufferLock(() => {
            this.commandBuffer.sound.frequency = frequency;
            this.commandBuffer.sound.duration = duration;
            this.commandBuffer.sound.timestamp = Date.now();
        });
    }

    // ===== SENSOR COMMANDS =====

    senseIR() {
        this.requestSensorData(0x06, 0x01); // CMD_READ_SENSOR with IR sensor type
        return this.getCachedSensorValue('ir');
    }

    senseUltrasonic() {
        this.requestSensorData(0x06, 0x01); // CMD_READ_SENSOR with ultrasonic sensor type  
        return this.getCachedSensorValue('ultrasonic');
    }

    senseButton() {
        this.requestSensorData(0x06, 0x04); // CMD_READ_SENSOR with button sensor type
        return Boolean(this.getCachedSensorValue('button'));
    }

    senseLight() {
        this.requestSensorData(0x06, 0x03); // CMD_READ_SENSOR with light sensor type
        return this.getCachedSensorValue('light');
    }

    senseTemperature() {
        this.requestSensorData(0x06, 0x05); // CMD_READ_SENSOR with temperature sensor type
        return this.getCachedSensorValue('temperature');
    }

    getAcceleration(args) {
        const axis = args.AXIS || 'x';
        this.requestSensorData(0x06, 0x06); // CMD_READ_SENSOR with acceleration sensor type
        return this.sensorCache.acceleration[axis] || 0;
    }

    getBatteryLevel() {
        this.requestSensorData(0x06, 0x07); // CMD_READ_SENSOR with battery sensor type
        return this.getCachedSensorValue('battery');
    }

    isObstacleDetected(args) {
        const distance = args.DISTANCE || 10;
        const ultrasonicValue = this.senseUltrasonic();
        return ultrasonicValue > 0 && ultrasonicValue <= distance;
    }

    requestSensorData(commandType, sensorType = 0) {
        this.acquireBufferLock(() => {
            // For sensor requests, we need to store both command and sensor type
            this.commandBuffer.sensors.requests.add({ command: commandType, sensor: sensorType });
            this.commandBuffer.sensors.timestamp = Date.now();
        });
    }

    getCachedSensorValue(sensorType) {
        const cache = this.sensorCache[sensorType];
        if (cache && (Date.now() - cache.timestamp) <= cache.maxAge) {
            return cache.value;
        }
        return 0; // Default value for expired/missing data
    }

    // ===== UTILITY COMMANDS =====

    calibrateSensors() {
        this.requestSensorData(0x09, 0x30); // CMD_CUSTOM with calibration parameter
        console.log('📱 Sensor calibration requested');
    }

    resetPosition() {
        this.requestSensorData(0x09, 0x31); // CMD_CUSTOM with reset parameter
        console.log('📱 Position reset requested');
    }

    // ===== HAT BLOCKS (EVENT-DRIVEN) =====

    whenDataReceived() {
        if (this.eventFlags.dataReceived) {
            this.eventFlags.dataReceived = false;
            return true;
        }
        return false;
    }

    whenSensorChanged(args) {
        const sensorType = args.SENSOR || 'ir';
        if (this.eventFlags.sensorChanged[sensorType]) {
            this.eventFlags.sensorChanged[sensorType] = false;
            return true;
        }
        return false;
    }

    whenButtonPressed() {
        if (this.eventFlags.buttonPressed) {
            this.eventFlags.buttonPressed = false;
            return true;
        }
        return false;
    }

    // ===== DATA PROCESSING =====

    processReceivedData(data) {
        try {
            if (data instanceof ArrayBuffer) {
                const packet = new Uint8Array(data);
                console.log(`📨 Received ${packet.length} bytes from TeBot server`);
                
                // Trigger data received event
                this.eventFlags.dataReceived = true;
                this.runtime.startHats('tebot_whenDataReceived');
                
                // Parse response packet
                if (packet.length >= 8 && packet[0] === 0xBB && packet[1] === 0x66) {
                    this.parseStructuredResponse(packet);
                } else {
                    this.parseLegacyResponse(packet);
                }
            }
        } catch (error) {
            console.error('❌ Error processing received data:', error);
        }
    }

    parseStructuredResponse(packet) {
        const requestId = packet[2] | (packet[3] << 8);
        const success = packet[4] === 0x01;
        const responseCount = packet[5];
        
        console.log(`📋 Structured response: ID=${requestId}, Success=${success}, Responses=${responseCount}`);
        
        // Parse sensor data from response
        let dataOffset = 8;
        for (let i = 0; i < responseCount && dataOffset + 8 <= packet.length; i++) {
            const sensorType = packet[dataOffset];
            const sensorValue = packet[dataOffset + 4];
            
            this.updateSensorCache(sensorType, sensorValue);
            dataOffset += 8;
        }
    }

    parseLegacyResponse(packet) {
        if (packet.length >= 8) {
            const sensorType = packet[0];
            const sensorValue = packet[4];
            this.updateSensorCache(sensorType, sensorValue);
        }
    }

    updateSensorCache(sensorType, value) {
        const now = Date.now();
        let sensorName = '';
        let oldValue = 0;
        
        switch (sensorType) {
            case 0x05:
                sensorName = 'ir';
                oldValue = this.sensorCache.ir.value;
                this.sensorCache.ir = { value, timestamp: now, maxAge: 100 };
                break;
            case 0x06:
                sensorName = 'ultrasonic';
                oldValue = this.sensorCache.ultrasonic.value;
                this.sensorCache.ultrasonic = { value, timestamp: now, maxAge: 100 };
                break;
            case 0x08:
                sensorName = 'button';
                oldValue = this.sensorCache.button.value;
                this.sensorCache.button = { value: Boolean(value), timestamp: now, maxAge: 100 };
                
                // Special handling for button press events
                if (value && !oldValue) {
                    this.eventFlags.buttonPressed = true;
                    this.runtime.startHats('tebot_whenButtonPressed');
                }
                break;
            case 0x09:
                sensorName = 'battery';
                oldValue = this.sensorCache.battery.value;
                this.sensorCache.battery = { value, timestamp: now, maxAge: 1000 };
                break;
            case 0x0A:
                sensorName = 'light';
                oldValue = this.sensorCache.light.value;
                this.sensorCache.light = { value, timestamp: now, maxAge: 100 };
                break;
            case 0x0B:
                sensorName = 'temperature';
                oldValue = this.sensorCache.temperature.value;
                this.sensorCache.temperature = { value, timestamp: now, maxAge: 100 };
                break;
            case 0x0C:
                sensorName = 'acceleration';
                // For acceleration, assume we get X, Y, Z in subsequent bytes
                this.sensorCache.acceleration = { 
                    x: value, 
                    y: 0, z: 0, // Would need to parse additional bytes
                    timestamp: now, 
                    maxAge: 100 
                };
                break;
        }
        
        // Trigger sensor change event if value actually changed
        if (sensorName && oldValue !== value) {
            this.eventFlags.sensorChanged[sensorName] = true;
            this.runtime.startHats('tebot_whenSensorChanged', { SENSOR: sensorName });
            console.log(`📊 ${sensorName} sensor changed: ${oldValue} → ${value}`);
        }
    }

    // ===== HELPER METHODS =====

    encodeLEDMatrix(pattern) {
        // Convert LED pattern string to byte array
        const rows = pattern.split(':');
        const bytes = [];
        
        for (let i = 0; i < Math.min(5, rows.length); i++) {
            let byte = 0;
            const row = rows[i];
            for (let j = 0; j < Math.min(5, row.length); j++) {
                if (row[j] === '1') {
                    byte |= (1 << (4 - j));
                }
            }
            bytes.push(byte);
        }
        
        // Pad to 5 bytes
        while (bytes.length < 5) {
            bytes.push(0);
        }
        
        return bytes;
    }

    textToLEDPattern(text) {
        // Simple text to LED pattern conversion
        // In a real implementation, you'd have a font lookup table
        const patterns = {
            'A': '01110:10001:11111:10001:10001',
            'B': '11110:10001:11110:10001:11110',
            'C': '01111:10000:10000:10000:01111',
            'H': '10001:10001:11111:10001:10001',
            'E': '11111:10000:11110:10000:11111',
            'L': '10000:10000:10000:10000:11111',
            'O': '01110:10001:10001:10001:01110'
        };
        
        const char = text.toUpperCase().charAt(0);
        return patterns[char] || '00000:00000:00000:00000:00000';
    }

    noteToFrequency(note) {
        const frequencies = {
            'C4': 262, 'D4': 294, 'E4': 330, 'F4': 349,
            'G4': 392, 'A4': 440, 'B4': 494, 'C5': 523
        };
        return frequencies[note] || 440;
    }
}

// Register the extension
Scratch.extensions.register(new Scratch3TeBotBlocks());
