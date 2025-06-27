// TeBot Scratch Extension - Fixed 8-byte Packet Protocol
// This version sends simple 8-byte commands that match the Arduino/Pico implementation

class Scratch3TeBotBlocks {
    constructor(runtime) {
        this.runtime = runtime;
        this.socket = null;
        
        // Sensor data cache
        this.sensorCache = {
            ir: { value: 0, timestamp: 0 },
            ultrasonic: { value: 0, timestamp: 0 },
            button: { value: false, timestamp: 0 },
            light: { value: 0, timestamp: 0 },
            battery: { value: 100, timestamp: 0 }
        };
        
        // Event flags for hat blocks
        this.eventFlags = {
            dataReceived: false,
            buttonPressed: false
        };
        
        this.lastSendTime = 0;
        this.minSendInterval = 100; // Minimum 100ms between commands
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

                // Movement Commands
                {
                    opcode: 'moveForward',
                    blockType: 'command',
                    text: 'move forward at speed [SPEED]%',
                    arguments: {
                        SPEED: {
                            type: 'number',
                            defaultValue: 50
                        }
                    }
                },
                {
                    opcode: 'moveBackward',
                    blockType: 'command',
                    text: 'move backward at speed [SPEED]%',
                    arguments: {
                        SPEED: {
                            type: 'number',
                            defaultValue: 50
                        }
                    }
                },
                {
                    opcode: 'turnLeft',
                    blockType: 'command',
                    text: 'turn left at speed [SPEED]%',
                    arguments: {
                        SPEED: {
                            type: 'number',
                            defaultValue: 50
                        }
                    }
                },
                {
                    opcode: 'turnRight',
                    blockType: 'command',
                    text: 'turn right at speed [SPEED]%',
                    arguments: {
                        SPEED: {
                            type: 'number',
                            defaultValue: 50
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
                    text: 'set robot speed to [SPEED]%',
                    arguments: {
                        SPEED: {
                            type: 'number',
                            defaultValue: 50
                        }
                    }
                },

                '---',

                // LED Commands
                {
                    opcode: 'setLED',
                    blockType: 'command',
                    text: 'turn LED [STATE] at brightness [BRIGHTNESS]%',
                    arguments: {
                        STATE: {
                            type: 'string',
                            menu: 'ledStates',
                            defaultValue: 'on'
                        },
                        BRIGHTNESS: {
                            type: 'number',
                            defaultValue: 100
                        }
                    }
                },

                '---',

                // Sound Commands
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

                '---',

                // Sensor Commands
                {
                    opcode: 'senseUltrasonic',
                    blockType: 'reporter',
                    text: 'ultrasonic distance'
                },
                {
                    opcode: 'senseIR',
                    blockType: 'reporter',
                    text: 'IR sensor value'
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
                    opcode: 'getBatteryLevel',
                    blockType: 'reporter',
                    text: 'battery level %'
                },

                '---',

                // Hat Blocks
                {
                    opcode: 'whenDataReceived',
                    blockType: 'hat',
                    text: 'when data received from TeBot'
                },
                {
                    opcode: 'whenButtonPressed',
                    blockType: 'hat',
                    text: 'when TeBot button pressed'
                }
            ],
            menus: {
                ledStates: {
                    acceptReporters: true,
                    items: [
                        { text: 'on', value: 'on' },
                        { text: 'off', value: 'off' }
                    ]
                }
            }
        };
    }

    // ===== CONNECTION MANAGEMENT =====

    openConnection() {
        console.log('Opening WebSocket connection to TeBot server...');
        
        if (this.socket && this.socket.readyState === WebSocket.OPEN) {
            console.log('WebSocket is already open');
            return;
        }

        this.socket = new WebSocket('ws://localhost:8080');
        this.socket.binaryType = 'arraybuffer';

        this.socket.onopen = () => {
            console.log('✅ Connected to TeBot server');
        };

        this.socket.onmessage = (event) => {
            this.processReceivedData(event.data);
        };

        this.socket.onclose = (event) => {
            console.log(`❌ TeBot connection closed: ${event.code}`);
        };

        this.socket.onerror = (error) => {
            console.error('❌ TeBot WebSocket error:', error);
        };
    }

    closeConnection() {
        console.log('Closing TeBot connection...');
        if (this.socket) {
            this.socket.close();
            this.socket = null;
        }
    }

    isConnected() {
        return this.socket && this.socket.readyState === WebSocket.OPEN;
    }

    // ===== COMMUNICATION =====

    sendCommand(cmdType, param1 = 0, param2 = 0, param3 = 0) {
        if (!this.isConnected()) {
            console.warn('Not connected to TeBot server');
            return;
        }

        // Rate limiting
        const now = Date.now();
        if (now - this.lastSendTime < this.minSendInterval) {
            console.warn('Command rate limited');
            return;
        }
        this.lastSendTime = now;

        // Create 80-byte packet with single 8-byte command
        const packet = new Uint8Array(80);
        
        // Fill with zeros
        packet.fill(0);
        
        // Set the first 8-byte command
        packet[0] = cmdType;
        packet[1] = param1 & 0xFF;
        packet[2] = param2 & 0xFF;
        packet[3] = param3 & 0xFF;
        packet[4] = 0; // param4
        packet[5] = 0; // param5
        packet[6] = 0; // param6
        packet[7] = 0; // param7

        this.socket.send(packet);
        console.log(`📤 Sent command: 0x${cmdType.toString(16).padStart(2, '0')} with params [${param1}, ${param2}, ${param3}]`);
    }

    // ===== MOVEMENT COMMANDS =====

    moveForward(args) {
        const speed = Math.max(0, Math.min(100, args.SPEED || 50));
        this.sendCommand(0x01, speed); // CMD_MOVE_FORWARD
    }

    moveBackward(args) {
        const speed = Math.max(0, Math.min(100, args.SPEED || 50));
        this.sendCommand(0x02, speed); // CMD_MOVE_BACKWARD
    }

    turnLeft(args) {
        const speed = Math.max(0, Math.min(100, args.SPEED || 50));
        this.sendCommand(0x03, speed); // CMD_TURN_LEFT
    }

    turnRight(args) {
        const speed = Math.max(0, Math.min(100, args.SPEED || 50));
        this.sendCommand(0x04, speed); // CMD_TURN_RIGHT
    }

    stop() {
        this.sendCommand(0x00); // CMD_STOP
    }

    setSpeed(args) {
        const speed = Math.max(0, Math.min(100, args.SPEED || 50));
        this.sendCommand(0x05, speed); // CMD_SET_SPEED
    }

    // ===== LED COMMANDS =====

    setLED(args) {
        const state = args.STATE === 'on' ? 1 : 0;
        const brightness = Math.max(0, Math.min(100, args.BRIGHTNESS || 100));
        this.sendCommand(0x07, state, brightness); // CMD_SET_LED
    }

    // ===== SOUND COMMANDS =====

    playTone(args) {
        const frequency = Math.max(50, Math.min(5000, args.FREQUENCY || 440));
        const duration = Math.max(50, Math.min(5000, args.DURATION || 500));
        
        // Split frequency into low and high bytes
        const freqLow = frequency & 0xFF;
        const freqHigh = (frequency >> 8) & 0xFF;
        const durationMs = Math.floor(duration / 10); // Convert to 10ms units
        
        this.sendCommand(0x08, freqLow, freqHigh, durationMs); // CMD_PLAY_SOUND
    }

    // ===== SENSOR COMMANDS =====

    senseUltrasonic() {
        this.sendCommand(0x06, 0x01); // CMD_READ_SENSOR, ultrasonic type
        return this.getCachedSensorValue('ultrasonic');
    }

    senseIR() {
        this.sendCommand(0x06, 0x02); // CMD_READ_SENSOR, IR type
        return this.getCachedSensorValue('ir');
    }

    senseButton() {
        this.sendCommand(0x06, 0x04); // CMD_READ_SENSOR, button type
        return Boolean(this.getCachedSensorValue('button'));
    }

    senseLight() {
        this.sendCommand(0x06, 0x03); // CMD_READ_SENSOR, light type
        return this.getCachedSensorValue('light');
    }

    getBatteryLevel() {
        this.sendCommand(0x0A); // CMD_STATUS_REQUEST
        return this.getCachedSensorValue('battery');
    }

    getCachedSensorValue(sensorType) {
        const cache = this.sensorCache[sensorType];
        if (cache && (Date.now() - cache.timestamp) <= 1000) { // 1 second cache
            return cache.value;
        }
        return 0; // Default value for expired/missing data
    }

    // ===== HAT BLOCKS =====

    whenDataReceived() {
        if (this.eventFlags.dataReceived) {
            this.eventFlags.dataReceived = false;
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
                
                // Process response (expect 80 bytes with multiple 8-byte responses)
                for (let i = 0; i < packet.length; i += 8) {
                    if (i + 8 <= packet.length) {
                        this.parseResponse(packet.slice(i, i + 8));
                    }
                }
            }
        } catch (error) {
            console.error('❌ Error processing received data:', error);
        }
    }

    parseResponse(responseBytes) {
        if (responseBytes.length < 8) return;
        
        const status = responseBytes[0];
        const direction = responseBytes[1];
        const speed = responseBytes[2];
        const battery = responseBytes[3];
        const distance = responseBytes[4];
        const ledState = responseBytes[5];
        const moving = responseBytes[6];
        const counter = responseBytes[7];
        
        // Update sensor cache
        const now = Date.now();
        
        // Check for sensor data response
        if (status === 0x10) {
            // This is a sensor data response
            const sensorType = responseBytes[1];
            const sensorValue = responseBytes[2] | (responseBytes[3] << 8);
            
            switch (sensorType) {
                case 0x01: // Ultrasonic
                    this.sensorCache.ultrasonic = { value: sensorValue, timestamp: now };
                    break;
                case 0x02: // IR
                    this.sensorCache.ir = { value: sensorValue, timestamp: now };
                    break;
                case 0x03: // Light
                    this.sensorCache.light = { value: sensorValue, timestamp: now };
                    break;
                case 0x04: // Button
                    const oldButton = this.sensorCache.button.value;
                    this.sensorCache.button = { value: Boolean(sensorValue), timestamp: now };
                    
                    // Trigger button pressed event
                    if (sensorValue && !oldButton) {
                        this.eventFlags.buttonPressed = true;
                        this.runtime.startHats('tebot_whenButtonPressed');
                    }
                    break;
            }
        } else {
            // Regular status response
            this.sensorCache.ultrasonic = { value: distance, timestamp: now };
            this.sensorCache.battery = { value: battery, timestamp: now };
            
            console.log(`📊 Status: direction=${direction}, speed=${speed}, battery=${battery}%, distance=${distance}cm`);
        }
    }
}

// Register the extension
Scratch.extensions.register(new Scratch3TeBotBlocks());
