/*
 * TeBot Arduino Implementation
 * 
 * This Arduino code implements the TeBot robot side that communicates with
 * the C# Windows Forms application via Bluetooth HC-05 module.
 * 
 * Hardware Requirements:
 * - Arduino Mega/Uno with multiple Serial ports (or SoftwareSerial)
 * - HC-05 Bluetooth module connected to Serial1
 * - Motors (2x DC motors with motor driver like L298N)
 * - Sensors: IR, Ultrasonic (HC-SR04), Button, Light sensor, Temperature sensor
 * - 5x5 LED matrix or individual LEDs
 * - Buzzer for sound
 * - Optional: Accelerometer (MPU6050)
 * 
 * Communication Protocol:
 * - Receives 80-byte packets from C# server
 * - Sends 8-byte response packets back
 * - Baud rate: 115200 (optimized for HC-05)
 */

#include <SoftwareSerial.h>

// ===== PIN DEFINITIONS =====
// Motors
#define MOTOR_LEFT_PWM    3
#define MOTOR_LEFT_DIR1   4
#define MOTOR_LEFT_DIR2   5
#define MOTOR_RIGHT_PWM   6
#define MOTOR_RIGHT_DIR1  7
#define MOTOR_RIGHT_DIR2  8

// Sensors
#define IR_SENSOR_PIN     A0
#define ULTRASONIC_TRIG   9
#define ULTRASONIC_ECHO   10
#define BUTTON_PIN        2
#define LIGHT_SENSOR_PIN  A1
#define TEMP_SENSOR_PIN   A2

// LEDs (5x5 matrix using shift registers or individual pins)
#define LED_DATA_PIN      11
#define LED_CLOCK_PIN     12
#define LED_LATCH_PIN     13

// Sound
#define BUZZER_PIN        A3

// Bluetooth (using Serial1 on Mega, or SoftwareSerial on Uno)
#define BT_RX_PIN         19  // For Serial1 on Mega
#define BT_TX_PIN         18  // For Serial1 on Mega

// ===== GLOBAL VARIABLES =====
// Robot state
struct RobotState {
  int speed = 50;           // Robot speed (0-100%)
  bool isMoving = false;
  int direction = 0;        // 0=stop, 1=forward, 2=backward, 3=left, 4=right
  
  // Sensor values
  int irValue = 0;
  int ultrasonicDistance = 0;
  bool buttonPressed = false;
  int lightValue = 0;
  int temperature = 0;
  int batteryLevel = 100;
  
  // LED matrix (5x5)
  byte ledMatrix[5] = {0, 0, 0, 0, 0};
  int ledBrightness = 100;
  
  // Sound
  int currentFrequency = 0;
  unsigned long soundEndTime = 0;
} robot;

// Communication buffers
byte receiveBuffer[80];
int receiveIndex = 0;
bool packetReady = false;

// Timing
unsigned long lastSensorRead = 0;
unsigned long lastStatusSend = 0;
const unsigned long SENSOR_INTERVAL = 50;   // Read sensors every 50ms
const unsigned long STATUS_INTERVAL = 100;  // Send status every 100ms

// ===== SETUP =====
void setup() {
  // Initialize Serial for debugging
  Serial.begin(115200);
  Serial.println("TeBot Arduino Starting...");
  
  // Initialize Bluetooth Serial
  Serial1.begin(115200);  // HC-05 configured for 115200 baud
  Serial.println("Bluetooth Serial initialized at 115200 baud");
  
  // Initialize pins
  initializePins();
  
  // Initialize sensors
  initializeSensors();
  
  // Initialize motors (stopped)
  stopMotors();
  
  // Initialize LED matrix
  clearLEDMatrix();
  
  // Show startup pattern
  showStartupPattern();
  
  Serial.println("TeBot Ready - Waiting for commands...");
}

// ===== MAIN LOOP =====
void loop() {
  unsigned long currentTime = millis();
  
  // Handle Bluetooth communication
  handleBluetoothData();
  
  // Read sensors periodically
  if (currentTime - lastSensorRead >= SENSOR_INTERVAL) {
    readAllSensors();
    lastSensorRead = currentTime;
  }
  
  // Send status updates periodically
  if (currentTime - lastStatusSend >= STATUS_INTERVAL) {
    sendStatusUpdate();
    lastStatusSend = currentTime;
  }
  
  // Handle sound timing
  handleSound(currentTime);
  
  // Process any received packets
  if (packetReady) {
    processReceivedPacket();
    packetReady = false;
  }
}

// ===== INITIALIZATION FUNCTIONS =====
void initializePins() {
  // Motor pins
  pinMode(MOTOR_LEFT_PWM, OUTPUT);
  pinMode(MOTOR_LEFT_DIR1, OUTPUT);
  pinMode(MOTOR_LEFT_DIR2, OUTPUT);
  pinMode(MOTOR_RIGHT_PWM, OUTPUT);
  pinMode(MOTOR_RIGHT_DIR1, OUTPUT);
  pinMode(MOTOR_RIGHT_DIR2, OUTPUT);
  
  // Sensor pins
  pinMode(ULTRASONIC_TRIG, OUTPUT);
  pinMode(ULTRASONIC_ECHO, INPUT);
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  
  // LED matrix pins
  pinMode(LED_DATA_PIN, OUTPUT);
  pinMode(LED_CLOCK_PIN, OUTPUT);
  pinMode(LED_LATCH_PIN, OUTPUT);
  
  // Buzzer pin
  pinMode(BUZZER_PIN, OUTPUT);
  
  Serial.println("Pins initialized");
}

void initializeSensors() {
  // Read initial sensor values
  readAllSensors();
  Serial.println("Sensors initialized");
}

void showStartupPattern() {
  // Display a startup pattern on LED matrix
  byte startupPattern[5] = {0x1F, 0x11, 0x11, 0x11, 0x1F}; // Rectangle
  memcpy(robot.ledMatrix, startupPattern, 5);
  updateLEDMatrix();
  
  // Play startup tone
  tone(BUZZER_PIN, 440, 200);
  delay(250);
  tone(BUZZER_PIN, 880, 200);
  delay(250);
  
  // Clear matrix
  clearLEDMatrix();
}

// ===== BLUETOOTH COMMUNICATION =====
void handleBluetoothData() {
  while (Serial1.available()) {
    byte receivedByte = Serial1.read();
    
    // Check for packet start (0xAA 0x55)
    if (receiveIndex == 0 && receivedByte == 0xAA) {
      receiveBuffer[receiveIndex++] = receivedByte;
    } else if (receiveIndex == 1 && receivedByte == 0x55) {
      receiveBuffer[receiveIndex++] = receivedByte;
    } else if (receiveIndex > 1 && receiveIndex < 80) {
      receiveBuffer[receiveIndex++] = receivedByte;
      
      // Check if we have a complete 80-byte packet
      if (receiveIndex >= 80) {
        // Verify checksum
        if (verifyPacketChecksum()) {
          packetReady = true;
          Serial.println("✓ Valid 80-byte packet received");
        } else {
          Serial.println("✗ Checksum failed");
        }
        receiveIndex = 0; // Reset for next packet
      }
    } else {
      // Invalid byte, reset
      receiveIndex = 0;
    }
  }
}

bool verifyPacketChecksum() {
  byte calculatedChecksum = 0;
  for (int i = 0; i < 72; i++) {
    calculatedChecksum ^= receiveBuffer[i];
  }
  return calculatedChecksum == receiveBuffer[72];
}

void processReceivedPacket() {
  // Extract packet information
  int requestId = receiveBuffer[2] | (receiveBuffer[3] << 8);
  int dataLength = receiveBuffer[4];
  
  Serial.print("Processing packet - ID: ");
  Serial.print(requestId);
  Serial.print(", Data length: ");
  Serial.println(dataLength);
  
  // Process commands starting from byte 8
  for (int i = 8; i < 8 + dataLength && i < 72; i++) {
    byte command = receiveBuffer[i];
    
    switch (command) {
      case 0x00: // Stop
        stopMotors();
        Serial.println("Command: Stop");
        break;
        
      case 0x01: // Move forward
        if (i + 1 < 8 + dataLength) {
          int steps = receiveBuffer[i + 1];
          moveForward(steps);
          Serial.print("Command: Move forward ");
          Serial.println(steps);
          i++; // Skip next byte
        }
        break;
        
      case 0x02: // Move backward
        if (i + 1 < 8 + dataLength) {
          int steps = receiveBuffer[i + 1];
          moveBackward(steps);
          Serial.print("Command: Move backward ");
          Serial.println(steps);
          i++;
        }
        break;
        
      case 0x03: // Turn left
        if (i + 1 < 8 + dataLength) {
          int degrees = receiveBuffer[i + 1];
          turnLeft(degrees);
          Serial.print("Command: Turn left ");
          Serial.println(degrees);
          i++;
        }
        break;
        
      case 0x04: // Turn right
        if (i + 1 < 8 + dataLength) {
          int degrees = receiveBuffer[i + 1];
          turnRight(degrees);
          Serial.print("Command: Turn right ");
          Serial.println(degrees);
          i++;
        }
        break;
        
      case 0x05: // IR sensor request
        sendSensorResponse(0x05, robot.irValue);
        Serial.println("Sensor request: IR");
        break;
        
      case 0x06: // Ultrasonic sensor request
        sendSensorResponse(0x06, robot.ultrasonicDistance);
        Serial.println("Sensor request: Ultrasonic");
        break;
        
      case 0x07: // LED matrix command
        if (i + 6 < 8 + dataLength) {
          robot.ledBrightness = receiveBuffer[i + 1];
          for (int j = 0; j < 5; j++) {
            robot.ledMatrix[j] = receiveBuffer[i + 2 + j];
          }
          updateLEDMatrix();
          Serial.println("Command: LED matrix update");
          i += 6;
        }
        break;
        
      case 0x08: // Button sensor request
        sendSensorResponse(0x08, robot.buttonPressed ? 1 : 0);
        Serial.println("Sensor request: Button");
        break;
        
      case 0x09: // Battery level request
        sendSensorResponse(0x09, robot.batteryLevel);
        Serial.println("Sensor request: Battery");
        break;
        
      case 0x0A: // Light sensor request
        sendSensorResponse(0x0A, robot.lightValue);
        Serial.println("Sensor request: Light");
        break;
        
      case 0x0B: // Temperature sensor request
        sendSensorResponse(0x0B, robot.temperature);
        Serial.println("Sensor request: Temperature");
        break;
        
      case 0x10: // Set speed
        if (i + 1 < 8 + dataLength) {
          robot.speed = receiveBuffer[i + 1];
          Serial.print("Command: Set speed ");
          Serial.println(robot.speed);
          i++;
        }
        break;
        
      case 0x20: // LED brightness
        if (i + 1 < 8 + dataLength) {
          robot.ledBrightness = receiveBuffer[i + 1];
          updateLEDMatrix();
          Serial.print("Command: LED brightness ");
          Serial.println(robot.ledBrightness);
          i++;
        }
        break;
        
      case 0x21: // Play tone
        if (i + 4 < 8 + dataLength) {
          int frequency = receiveBuffer[i + 1] | (receiveBuffer[i + 2] << 8);
          int duration = receiveBuffer[i + 3] | (receiveBuffer[i + 4] << 8);
          playTone(frequency, duration);
          Serial.print("Command: Play tone ");
          Serial.print(frequency);
          Serial.print("Hz for ");
          Serial.print(duration);
          Serial.println("ms");
          i += 4;
        }
        break;
        
      case 0x30: // Calibrate sensors
        calibrateSensors();
        Serial.println("Command: Calibrate sensors");
        break;
        
      case 0x31: // Reset position
        resetPosition();
        Serial.println("Command: Reset position");
        break;
    }
  }
  
  // Send acknowledgment
  sendAcknowledgment(requestId, true);
}

void sendSensorResponse(byte sensorType, int value) {
  byte response[8];
  response[0] = sensorType;     // Sensor type
  response[1] = 0x01;           // Response indicator
  response[2] = 0x00;           // Request ID (low) - could be improved
  response[3] = 0x00;           // Request ID (high)
  response[4] = value & 0xFF;   // Sensor value (low byte)
  response[5] = (value >> 8) & 0xFF; // Sensor value (high byte)
  response[6] = 0x00;           // Reserved
  response[7] = 0x00;           // Reserved
  
  Serial1.write(response, 8);
  
  Serial.print("Sent sensor response: Type=0x");
  Serial.print(sensorType, HEX);
  Serial.print(", Value=");
  Serial.println(value);
}

void sendAcknowledgment(int requestId, bool success) {
  byte ack[8];
  ack[0] = 0xFF;                    // Acknowledgment marker
  ack[1] = success ? 0x01 : 0x00;   // Success flag
  ack[2] = requestId & 0xFF;        // Request ID (low)
  ack[3] = (requestId >> 8) & 0xFF; // Request ID (high)
  ack[4] = 0x00;                    // Reserved
  ack[5] = 0x00;                    // Reserved
  ack[6] = 0x00;                    // Reserved
  ack[7] = 0x00;                    // Reserved
  
  Serial1.write(ack, 8);
}

void sendStatusUpdate() {
  // Send periodic status update with key sensor values
  byte status[8];
  status[0] = 0xFE;                 // Status update marker
  status[1] = robot.isMoving ? 1 : 0;
  status[2] = robot.irValue & 0xFF;
  status[3] = robot.ultrasonicDistance & 0xFF;
  status[4] = robot.buttonPressed ? 1 : 0;
  status[5] = robot.batteryLevel;
  status[6] = robot.speed;
  status[7] = robot.direction;
  
  Serial1.write(status, 8);
}

// ===== MOTOR CONTROL =====
void stopMotors() {
  digitalWrite(MOTOR_LEFT_DIR1, LOW);
  digitalWrite(MOTOR_LEFT_DIR2, LOW);
  digitalWrite(MOTOR_RIGHT_DIR1, LOW);
  digitalWrite(MOTOR_RIGHT_DIR2, LOW);
  analogWrite(MOTOR_LEFT_PWM, 0);
  analogWrite(MOTOR_RIGHT_PWM, 0);
  
  robot.isMoving = false;
  robot.direction = 0;
}

void moveForward(int steps) {
  int pwmValue = map(robot.speed, 0, 100, 0, 255);
  
  digitalWrite(MOTOR_LEFT_DIR1, HIGH);
  digitalWrite(MOTOR_LEFT_DIR2, LOW);
  digitalWrite(MOTOR_RIGHT_DIR1, HIGH);
  digitalWrite(MOTOR_RIGHT_DIR2, LOW);
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  
  robot.isMoving = true;
  robot.direction = 1;
  
  // For simplicity, move for a time proportional to steps
  // In a real implementation, you'd use encoders
  if (steps > 0) {
    delay(steps * 10); // 10ms per step
    stopMotors();
  }
}

void moveBackward(int steps) {
  int pwmValue = map(robot.speed, 0, 100, 0, 255);
  
  digitalWrite(MOTOR_LEFT_DIR1, LOW);
  digitalWrite(MOTOR_LEFT_DIR2, HIGH);
  digitalWrite(MOTOR_RIGHT_DIR1, LOW);
  digitalWrite(MOTOR_RIGHT_DIR2, HIGH);
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  
  robot.isMoving = true;
  robot.direction = 2;
  
  if (steps > 0) {
    delay(steps * 10);
    stopMotors();
  }
}

void turnLeft(int degrees) {
  int pwmValue = map(robot.speed, 0, 100, 0, 255);
  
  digitalWrite(MOTOR_LEFT_DIR1, LOW);
  digitalWrite(MOTOR_LEFT_DIR2, HIGH);
  digitalWrite(MOTOR_RIGHT_DIR1, HIGH);
  digitalWrite(MOTOR_RIGHT_DIR2, LOW);
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  
  robot.isMoving = true;
  robot.direction = 3;
  
  if (degrees > 0) {
    delay(degrees * 5); // 5ms per degree (approximate)
    stopMotors();
  }
}

void turnRight(int degrees) {
  int pwmValue = map(robot.speed, 0, 100, 0, 255);
  
  digitalWrite(MOTOR_LEFT_DIR1, HIGH);
  digitalWrite(MOTOR_LEFT_DIR2, LOW);
  digitalWrite(MOTOR_RIGHT_DIR1, LOW);
  digitalWrite(MOTOR_RIGHT_DIR2, HIGH);
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  
  robot.isMoving = true;
  robot.direction = 4;
  
  if (degrees > 0) {
    delay(degrees * 5);
    stopMotors();
  }
}

// ===== SENSOR READING =====
void readAllSensors() {
  // Read IR sensor
  robot.irValue = analogRead(IR_SENSOR_PIN);
  
  // Read ultrasonic sensor
  robot.ultrasonicDistance = readUltrasonicDistance();
  
  // Read button
  robot.buttonPressed = !digitalRead(BUTTON_PIN); // Inverted due to pullup
  
  // Read light sensor
  robot.lightValue = analogRead(LIGHT_SENSOR_PIN);
  
  // Read temperature (assuming TMP36 or similar)
  robot.temperature = readTemperature();
  
  // Simulate battery level (could be connected to voltage divider)
  robot.batteryLevel = map(analogRead(A5), 0, 1023, 0, 100);
}

int readUltrasonicDistance() {
  digitalWrite(ULTRASONIC_TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(ULTRASONIC_TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(ULTRASONIC_TRIG, LOW);
  
  long duration = pulseIn(ULTRASONIC_ECHO, HIGH, 30000); // 30ms timeout
  if (duration == 0) return 255; // No echo received
  
  int distance = duration * 0.034 / 2; // Convert to cm
  return min(distance, 255); // Cap at 255cm
}

int readTemperature() {
  int reading = analogRead(TEMP_SENSOR_PIN);
  // Assuming TMP36: Vout = (Temp in °C * 10mV) + 500mV
  float voltage = reading * (5.0 / 1024.0);
  float temperature = (voltage - 0.5) * 100;
  return (int)temperature;
}

// ===== LED MATRIX CONTROL =====
void updateLEDMatrix() {
  // Simple implementation using shift registers
  // Adjust brightness using PWM if needed
  for (int row = 0; row < 5; row++) {
    shiftOut(LED_DATA_PIN, LED_CLOCK_PIN, MSBFIRST, robot.ledMatrix[row]);
  }
  digitalWrite(LED_LATCH_PIN, HIGH);
  delayMicroseconds(1);
  digitalWrite(LED_LATCH_PIN, LOW);
}

void clearLEDMatrix() {
  for (int i = 0; i < 5; i++) {
    robot.ledMatrix[i] = 0;
  }
  updateLEDMatrix();
}

// ===== SOUND CONTROL =====
void playTone(int frequency, int duration) {
  if (frequency > 0 && duration > 0) {
    robot.currentFrequency = frequency;
    robot.soundEndTime = millis() + duration;
    tone(BUZZER_PIN, frequency);
  }
}

void handleSound(unsigned long currentTime) {
  if (robot.currentFrequency > 0 && currentTime >= robot.soundEndTime) {
    noTone(BUZZER_PIN);
    robot.currentFrequency = 0;
  }
}

// ===== UTILITY FUNCTIONS =====
void calibrateSensors() {
  Serial.println("Calibrating sensors...");
  
  // Simple calibration - could be more sophisticated
  delay(100);
  readAllSensors();
  
  // Flash LEDs to indicate calibration
  for (int i = 0; i < 3; i++) {
    for (int j = 0; j < 5; j++) {
      robot.ledMatrix[j] = 0x1F;
    }
    updateLEDMatrix();
    delay(200);
    clearLEDMatrix();
    delay(200);
  }
  
  Serial.println("Sensor calibration complete");
}

void resetPosition() {
  Serial.println("Resetting position...");
  
  // Stop all movement
  stopMotors();
  
  // Reset any position tracking variables
  // (In a real implementation, you'd reset encoder counts, etc.)
  
  // Visual feedback
  byte pattern[5] = {0x04, 0x0E, 0x1F, 0x0E, 0x04}; // Diamond
  memcpy(robot.ledMatrix, pattern, 5);
  updateLEDMatrix();
  delay(500);
  clearLEDMatrix();
  
  Serial.println("Position reset complete");
}

// ===== DEBUGGING =====
void printRobotStatus() {
  Serial.println("=== Robot Status ===");
  Serial.print("Speed: "); Serial.println(robot.speed);
  Serial.print("Moving: "); Serial.println(robot.isMoving ? "Yes" : "No");
  Serial.print("Direction: "); Serial.println(robot.direction);
  Serial.print("IR: "); Serial.println(robot.irValue);
  Serial.print("Ultrasonic: "); Serial.print(robot.ultrasonicDistance); Serial.println("cm");
  Serial.print("Button: "); Serial.println(robot.buttonPressed ? "Pressed" : "Released");
  Serial.print("Light: "); Serial.println(robot.lightValue);
  Serial.print("Temperature: "); Serial.print(robot.temperature); Serial.println("°C");
  Serial.print("Battery: "); Serial.print(robot.batteryLevel); Serial.println("%");
  Serial.println("==================");
}
