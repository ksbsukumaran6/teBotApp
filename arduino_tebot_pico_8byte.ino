/*
 * TeBot Raspberry Pi Pico Implementation - 8-byte Packet Protocol
 * 
 * This Arduino code implements the TeBot robot side that communicates with
 * the C# Windows Forms application via Bluetooth HC-05 module.
 * 
 * UPDATED PROTOCOL:
 * - Receives individual 8-byte command packets from C# system
 * - C# splits 80-byte Scratch input into 10×8-byte packets
 * - Each 8-byte packet = 1 command [type, param1, param2, param3, param4, param5, param6, param7]
 * - Responds with 8-byte status packets back to C#
 * - Baud rate: 115200 (matches C# BluetoothManager)
 * 
 * Hardware Requirements:
 * - Raspberry Pi Pico (RP2040) with Arduino framework
 * - HC-05 Bluetooth module connected to Serial1 (UART1: GP0/GP1)
 * - Motors (2x DC motors with motor driver like L298N)
 * - Sensors: IR, Ultrasonic (HC-SR04), Button, Light sensor
 * - LEDs and Buzzer for feedback
 */

#include <Arduino.h>

// ===== PIN DEFINITIONS (Raspberry Pi Pico GPIO) =====
// Motors - Pins 8, 9, 10, 11
#define MOTOR_LEFT_PWM    8    // GP8
#define MOTOR_LEFT_DIR1   9    // GP9
#define MOTOR_RIGHT_PWM   10   // GP10
#define MOTOR_RIGHT_DIR1  11   // GP11

// Ultrasonic Sensor - Pins 14, 15
#define ULTRASONIC_ECHO   14   // GP14
#define ULTRASONIC_TRIG   15   // GP15

// Buttons - Pins 22, 23
#define BUTTON_A_PIN      22   // GP22 (Button A)
#define BUTTON_B_PIN      23   // GP23 (Button B)

// IR Sensors - A2, A3 (GP28, GP29 - ADC channels)
#define IR_LEFT_PIN       28   // GP28 (ADC2 - A2)
#define IR_RIGHT_PIN      29   // GP29 (ADC3 - A3)

// LEDs and Sound
#define LED_PIN           25   // GP25 (Built-in LED)
#define BUZZER_PIN        24   // GP24 (Speaker)
#define NEOPIXEL_PIN      2    // GP2 (Neopixel LEDs)

// LCD I2C - Pins 4, 5
#define LCD_SDA_PIN       4    // GP4 (SDA)
#define LCD_SCL_PIN       5    // GP5 (SCL)

// Bluetooth (using Serial1 on Pico - UART1: GP0=TX, GP1=RX)
#define BT_SERIAL Serial1

// ===== PROTOCOL CONSTANTS =====
const int PACKET_SIZE = 8;        // Each packet is 8 bytes
const int RESPONSE_SIZE = 8;      // Response packet size
const int COMMAND_TIMEOUT = 1000; // Command timeout in ms

// Command types (from C# TeBot system)
const uint8_t CMD_STOP = 0x00;
const uint8_t CMD_MOVE_FORWARD = 0x01;
const uint8_t CMD_MOVE_BACKWARD = 0x02;
const uint8_t CMD_TURN_LEFT = 0x03;
const uint8_t CMD_TURN_RIGHT = 0x04;
const uint8_t CMD_SET_SPEED = 0x05;
const uint8_t CMD_READ_SENSOR = 0x06;
const uint8_t CMD_SET_LED = 0x07;
const uint8_t CMD_PLAY_SOUND = 0x08;
const uint8_t CMD_CUSTOM = 0x09;
const uint8_t CMD_STATUS_REQUEST = 0x0A;

// ===== GLOBAL VARIABLES =====
// Robot state
struct RobotState {
  int speed = 50;               // Robot speed (0-100%)
  bool isMoving = false;
  int direction = 0;            // 0=stop, 1=forward, 2=backward, 3=left, 4=right
  
  // Sensor values
  int irValue = 0;
  int ultrasonicDistance = 0;
  bool buttonPressed = false;
  int lightValue = 0;
  int batteryLevel = 100;
  
  // LED state
  bool ledOn = false;
  int ledBrightness = 100;
  
  // Sound state
  int currentFrequency = 0;
  unsigned long soundEndTime = 0;
} robot;

// Communication buffers
uint8_t commandBuffer[PACKET_SIZE];
int bufferIndex = 0;
bool commandReady = false;

// Timing and status
unsigned long lastSensorRead = 0;
unsigned long lastCommandTime = 0;
unsigned long commandCount = 0;
const unsigned long SENSOR_INTERVAL = 100;   // Read sensors every 100ms

// ===== FUNCTION DECLARATIONS =====
void initializePins();
void handleBluetoothData();
void processCommand();
void stopMotors();
void moveForward(int speed);
void moveBackward(int speed);
void turnLeft(int speed);
void turnRight(int speed);
void readAllSensors();
int readUltrasonicDistance();
void playSound(int frequency, int duration);
void sendStatusResponse(uint8_t status);
void sendSensorData(uint8_t sensorType);

// ===== SETUP =====
void setup() {
  // Initialize Serial for debugging
  Serial.begin(115200);
  Serial.println("TeBot Raspberry Pi Pico Starting (8-byte packet protocol)...");
  
  // Initialize Bluetooth Serial (UART1 on Pico)
  Serial1.begin(115200); // Keep 115200 to match C# BluetoothManager
  Serial.println("Using Serial1 (UART1) for Bluetooth at 115200 baud");
  Serial.println("Bluetooth: GP0=TX, GP1=RX");
  
  // Initialize pins
  initializePins();
  
  // Initialize sensors and outputs
  robot.batteryLevel = 100;
  robot.isMoving = false;
  
  // Stop motors and turn off LED
  stopMotors();
  digitalWrite(LED_PIN, LOW);
  
  // Send startup status
  sendStatusResponse(0x01); // Startup complete
  
  Serial.println("TeBot Ready - Waiting for 8-byte commands...");
  Serial.println("Expected packet format: [cmd_type, param1, param2, param3, param4, param5, param6, param7]");
}

// ===== MAIN LOOP =====
void loop() {
  unsigned long currentTime = millis();
  
  // Handle incoming Bluetooth data
  handleBluetoothData();
  
  // Read sensors periodically
  if (currentTime - lastSensorRead >= SENSOR_INTERVAL) {
    readAllSensors();
    lastSensorRead = currentTime;
  }
  
  // Handle sound timing
  if (robot.currentFrequency > 0 && currentTime >= robot.soundEndTime) {
    noTone(BUZZER_PIN);
    robot.currentFrequency = 0;
  }
  
  // Process any received commands
  if (commandReady) {
    processCommand();
    commandReady = false;
  }
  
  // Auto-stop if no commands received for a while
  if (robot.isMoving && (currentTime - lastCommandTime > COMMAND_TIMEOUT)) {
    stopMotors();
    Serial.println("Auto-stop: No commands received");
  }
}

// ===== INITIALIZATION =====
void initializePins() {
  // Motor pins (single direction pin per motor for simplified control)
  pinMode(MOTOR_LEFT_PWM, OUTPUT);
  pinMode(MOTOR_LEFT_DIR1, OUTPUT);
  pinMode(MOTOR_RIGHT_PWM, OUTPUT);
  pinMode(MOTOR_RIGHT_DIR1, OUTPUT);
  
  // Sensor pins
  pinMode(ULTRASONIC_TRIG, OUTPUT);
  pinMode(ULTRASONIC_ECHO, INPUT);
  pinMode(BUTTON_A_PIN, INPUT_PULLUP);
  pinMode(BUTTON_B_PIN, INPUT_PULLUP);
  
  // Output pins
  pinMode(LED_PIN, OUTPUT);
  pinMode(BUZZER_PIN, OUTPUT);
  
  Serial.println("Pins initialized for Raspberry Pi Pico");
}

// ===== BLUETOOTH COMMUNICATION =====
void handleBluetoothData() {
  while (BT_SERIAL.available()) {
    uint8_t receivedByte = BT_SERIAL.read();
    
    // Simple 8-byte packet collection
    commandBuffer[bufferIndex++] = receivedByte;
    
    // Check if we have a complete 8-byte command
    if (bufferIndex >= PACKET_SIZE) {
      commandReady = true;
      bufferIndex = 0;
      lastCommandTime = millis();
      commandCount++;
      
      // Debug output
      Serial.print("Command #");
      Serial.print(commandCount);
      Serial.print(" received: ");
      for (int i = 0; i < PACKET_SIZE; i++) {
        Serial.print("0x");
        if (commandBuffer[i] < 16) Serial.print("0");
        Serial.print(commandBuffer[i], HEX);
        Serial.print(" ");
      }
      Serial.println();
    }
  }
}

void processCommand() {
  uint8_t cmdType = commandBuffer[0];
  uint8_t param1 = commandBuffer[1];
  uint8_t param2 = commandBuffer[2];
  uint8_t param3 = commandBuffer[3];
  // param4-7 available for extended commands
  
  Serial.print("Processing command type: 0x");
  Serial.println(cmdType, HEX);
  
  switch (cmdType) {
    case CMD_STOP:
      stopMotors();
      sendStatusResponse(0x00); // Success
      break;
      
    case CMD_MOVE_FORWARD:
      moveForward(param1 > 0 ? param1 : robot.speed);
      sendStatusResponse(0x00);
      break;
      
    case CMD_MOVE_BACKWARD:
      moveBackward(param1 > 0 ? param1 : robot.speed);
      sendStatusResponse(0x00);
      break;
      
    case CMD_TURN_LEFT:
      turnLeft(param1 > 0 ? param1 : robot.speed);
      sendStatusResponse(0x00);
      break;
      
    case CMD_TURN_RIGHT:
      turnRight(param1 > 0 ? param1 : robot.speed);
      sendStatusResponse(0x00);
      break;
      
    case CMD_SET_SPEED:
      robot.speed = constrain(param1, 0, 100);
      Serial.print("Speed set to: ");
      Serial.println(robot.speed);
      sendStatusResponse(0x00);
      break;
      
    case CMD_READ_SENSOR:
      sendSensorData(param1); // param1 = sensor type
      break;
      
    case CMD_SET_LED:
      robot.ledOn = (param1 > 0);
      robot.ledBrightness = param2;
      digitalWrite(LED_PIN, robot.ledOn ? HIGH : LOW);
      sendStatusResponse(0x00);
      break;
      
    case CMD_PLAY_SOUND:
      playSound(param1 | (param2 << 8), param3 * 10); // frequency, duration
      sendStatusResponse(0x00);
      break;
      
    case CMD_STATUS_REQUEST:
      sendStatusResponse(0x00);
      break;
      
    default:
      Serial.print("Unknown command: 0x");
      Serial.println(cmdType, HEX);
      sendStatusResponse(0xFF); // Error: unknown command
      break;
  }
}

// ===== MOTOR CONTROL =====
void stopMotors() {
  digitalWrite(MOTOR_LEFT_DIR1, LOW);
  digitalWrite(MOTOR_RIGHT_DIR1, LOW);
  analogWrite(MOTOR_LEFT_PWM, 0);
  analogWrite(MOTOR_RIGHT_PWM, 0);
  robot.isMoving = false;
  robot.direction = 0;
  Serial.println("Motors stopped");
}

void moveForward(int speed) {
  int pwmValue = map(speed, 0, 100, 0, 255);
  digitalWrite(MOTOR_LEFT_DIR1, HIGH);   // Forward direction
  digitalWrite(MOTOR_RIGHT_DIR1, HIGH);  // Forward direction
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  robot.isMoving = true;
  robot.direction = 1;
  Serial.print("Moving forward at speed: ");
  Serial.println(speed);
}

void moveBackward(int speed) {
  int pwmValue = map(speed, 0, 100, 0, 255);
  digitalWrite(MOTOR_LEFT_DIR1, LOW);    // Reverse direction
  digitalWrite(MOTOR_RIGHT_DIR1, LOW);   // Reverse direction
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  robot.isMoving = true;
  robot.direction = 2;
  Serial.print("Moving backward at speed: ");
  Serial.println(speed);
}

void turnLeft(int speed) {
  int pwmValue = map(speed, 0, 100, 0, 255);
  digitalWrite(MOTOR_LEFT_DIR1, LOW);    // Left motor reverse
  digitalWrite(MOTOR_RIGHT_DIR1, HIGH);  // Right motor forward
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  robot.isMoving = true;
  robot.direction = 3;
  Serial.print("Turning left at speed: ");
  Serial.println(speed);
}

void turnRight(int speed) {
  int pwmValue = map(speed, 0, 100, 0, 255);
  digitalWrite(MOTOR_LEFT_DIR1, HIGH);   // Left motor forward
  digitalWrite(MOTOR_RIGHT_DIR1, LOW);   // Right motor reverse
  analogWrite(MOTOR_LEFT_PWM, pwmValue);
  analogWrite(MOTOR_RIGHT_PWM, pwmValue);
  robot.isMoving = true;
  robot.direction = 4;
  Serial.print("Turning right at speed: ");
  Serial.println(speed);
}

// ===== SENSOR FUNCTIONS =====
void readAllSensors() {
  // Read ultrasonic sensor
  robot.ultrasonicDistance = readUltrasonicDistance();
  
  // Read IR sensors (left and right)
  int irLeft = analogRead(IR_LEFT_PIN);
  int irRight = analogRead(IR_RIGHT_PIN);
  robot.irValue = (irLeft + irRight) / 2; // Average of both IR sensors
  
  // Read buttons (A and B)
  bool buttonA = !digitalRead(BUTTON_A_PIN); // Pullup, so inverted
  bool buttonB = !digitalRead(BUTTON_B_PIN); // Pullup, so inverted
  robot.buttonPressed = buttonA || buttonB;  // Either button pressed
  
  // Simulate light sensor (could add real light sensor later)
  robot.lightValue = analogRead(IR_LEFT_PIN); // Using IR as light proxy for now
  
  // Simulate battery level (could be real ADC reading from voltage divider)
  robot.batteryLevel = constrain(robot.batteryLevel, 80, 100);
}

int readUltrasonicDistance() {
  digitalWrite(ULTRASONIC_TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(ULTRASONIC_TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(ULTRASONIC_TRIG, LOW);
  
  long duration = pulseIn(ULTRASONIC_ECHO, HIGH, 30000); // 30ms timeout
  if (duration == 0) return 999; // No echo received
  
  int distance = duration * 0.034 / 2;
  return constrain(distance, 0, 400);
}

// ===== SOUND CONTROL =====
void playSound(int frequency, int duration) {
  if (frequency > 0 && duration > 0) {
    tone(BUZZER_PIN, frequency, duration);
    robot.currentFrequency = frequency;
    robot.soundEndTime = millis() + duration;
    Serial.print("Playing sound: ");
    Serial.print(frequency);
    Serial.print(" Hz for ");
    Serial.print(duration);
    Serial.println(" ms");
  }
}

// ===== RESPONSE FUNCTIONS =====
void sendStatusResponse(uint8_t status) {
  uint8_t response[RESPONSE_SIZE];
  response[0] = status;                    // Status code
  response[1] = robot.direction;           // Current direction
  response[2] = robot.speed;               // Current speed
  response[3] = robot.batteryLevel;        // Battery level
  response[4] = robot.ultrasonicDistance; // Distance sensor
  response[5] = robot.ledOn ? 1 : 0;       // LED state
  response[6] = robot.isMoving ? 1 : 0;    // Movement state
  response[7] = commandCount & 0xFF;       // Command counter (low byte)
  
  BT_SERIAL.write(response, RESPONSE_SIZE);
  
  // Debug output
  Serial.print("Sent response: ");
  for (int i = 0; i < RESPONSE_SIZE; i++) {
    Serial.print("0x");
    if (response[i] < 16) Serial.print("0");
    Serial.print(response[i], HEX);
    Serial.print(" ");
  }
  Serial.println();
}

void sendSensorData(uint8_t sensorType) {
  uint8_t response[RESPONSE_SIZE];
  response[0] = 0x10; // Sensor data response
  response[1] = sensorType;
  
  switch (sensorType) {
    case 0x01: // Ultrasonic
      response[2] = robot.ultrasonicDistance & 0xFF;
      response[3] = (robot.ultrasonicDistance >> 8) & 0xFF;
      break;
    case 0x02: // IR sensor
      response[2] = robot.irValue & 0xFF;
      response[3] = (robot.irValue >> 8) & 0xFF;
      break;
    case 0x03: // Light sensor
      response[2] = robot.lightValue & 0xFF;
      response[3] = (robot.lightValue >> 8) & 0xFF;
      break;
    case 0x04: // Button
      response[2] = robot.buttonPressed ? 1 : 0;
      response[3] = 0;
      break;
    default:
      response[2] = 0xFF; // Unknown sensor
      response[3] = 0xFF;
      break;
  }
  
  response[4] = robot.batteryLevel;
  response[5] = 0; // Reserved
  response[6] = 0; // Reserved
  response[7] = 0; // Checksum (simple XOR)
  
  for (int i = 0; i < 7; i++) {
    response[7] ^= response[i];
  }
  
  BT_SERIAL.write(response, RESPONSE_SIZE);
  Serial.print("Sent sensor data for type 0x");
  Serial.println(sensorType, HEX);
}
