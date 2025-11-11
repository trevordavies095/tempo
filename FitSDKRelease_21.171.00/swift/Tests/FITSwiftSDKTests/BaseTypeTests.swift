/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import XCTest
@testable import FITSwiftSDK

final class BaseTypeTests: XCTestCase {

    func test_isValid_whenBaseTypeIsString() {
        let baseType = BaseType.STRING
        let testCases = [
            (title: "String - Valid", valid: true, string: "This is a somewhat long string"),
            (title: "String - 255 bytes + NULL Terminator - Invalid", valid: false, string: "AS4EgyRNHimg4Pw3bUiFQwGyOttIQti8kHzPcfoUQ1kxi4PGVpwuE7MVlfnA0PjvIdWYn"
             + "L5yDX4LmULwXFTt8jGqfafPSoL3CXmYVGaTHuB1ILbjdVtPGPm0FQPyS6NVeJ97cBYI6PoVI7wmRnc7MLS903ckhJephd"
             + "Y1OdBKJ4YRWTmhrR712BSl59SEwDs6uLHLUvWnA6JE6aVPkN2LJbI11QAtKzXNORWcK2ggsWqtsAzxSsdGyXCs6qs6CDx"),
            (title: "String 254 bytes + NULL Terminator - Valid", valid: true, string: "这套动作由两组 4 分钟的 Tabata 训练组成，中间休息"
             + "1 分钟。对于每组 Tabata 训练，在 训练的 20 秒内尽可能多地重复完成动作，休息 "
             + "10 秒，然后重复动作总共 8 组。在 Tabata 训练中间，还有 1 分"),
            (title: "String 255 bytes + NULL Terminator - Invalid", valid: false, string: "这套动作由两组 4 分钟的 Tabata 训练组成，中间休息"
             + "1 分钟。对于每组 Tabata 训练，在 训练的 20 秒内尽可能多地重复完成动作，休息 "
             + "10 秒，然后重复动作总共 8 组。在 Tabata 训练中间，还有 1 分."),
            (title: "Empty String - Invalid", valid: false, string: ""),
        ]
        
        for (title, valid, string) in testCases {
            XCTContext.runActivity(named: title) { activity in
                XCTAssertEqual(baseType.isValid(string), valid)
                XCTAssertEqual(baseType.isInvalid(string), !valid)
            }
        }
    }

    func test_correctRangeAndType_whenTypeIs1Byte() {
        let testCases = [
            (title: "Byte Valid", baseType: .BYTE, value: 10, expectedValue: 10),
            (title: "Byte Max Invalid", baseType: .BYTE, value: Float64(UInt8.max) + 1, expectedValue: Float64(BaseType.BYTE.invalidValue() as UInt8)),
            (title: "Byte Min Invalid", baseType: .BYTE, value: Float64(UInt8.min) - 1, expectedValue: Float64(BaseType.BYTE.invalidValue() as UInt8)),
            (title: "Enum Valid", baseType: .ENUM, value: 10, expectedValue: 10),
            (title: "Enum Max Invalid", baseType: .ENUM, value: Float64(UInt8.max) + 1, expectedValue: Float64(BaseType.ENUM.invalidValue() as UInt8)),
            (title: "Enum Min Invalid", baseType: .ENUM, value: Float64(UInt8.min) - 1, expectedValue: Float64(BaseType.ENUM.invalidValue() as UInt8)),
            (title: "SInt8 Valid", baseType: .SINT8, value: -10, expectedValue: -10),
            (title: "SInt8 Max Invalid", baseType: .SINT8, value: Float64(Int8.max) + 1, expectedValue: Float64(BaseType.SINT8.invalidValue() as Int8)),
            (title: "SInt8 Min Invalid", baseType: .SINT8, value: Float64(Int8.min) - 1, expectedValue: Float64(BaseType.SINT8.invalidValue() as Int8)),
            (title: "UInt8 Valid", baseType: .UINT8, value: 10, expectedValue: 10),
            (title: "UInt8 Max Invalid", baseType: .UINT8, value: Float64(UInt8.max) + 1, expectedValue: Float64(BaseType.UINT8.invalidValue() as UInt8)),
            (title: "UInt8 Min Invalid", baseType: .UINT8, value: Float64(UInt8.min) - 1, expectedValue: Float64(BaseType.UINT8.invalidValue() as UInt8)),
        ] as [(String, BaseType, Float64, Float64)]
        
        assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: testCases)
    }
    
    func test_correctRangeAndType_whenTypeIs2Bytes() {
        let testCases = [
            (title: "SInt16 Valid", baseType: .SINT16, value: -300, expectedValue: -300),
            (title: "SInt16 Max Invalid", baseType: .SINT16, value: Float64(Int16.max) + 1, expectedValue: Float64(BaseType.SINT16.invalidValue() as Int16)),
            (title: "SInt16 Min Invalid", baseType: .SINT16, value: Float64(Int16.min) - 1, expectedValue: Float64(BaseType.SINT16.invalidValue() as Int16)),
            (title: "UInt16 Valid", baseType: .UINT16, value: 10, expectedValue: 10),
            (title: "UInt16 Max Invalid", baseType: .UINT16, value: Float64(UInt16.max) + 1, expectedValue: Float64(BaseType.UINT16.invalidValue() as UInt16)),
            (title: "UInt16 Min Invalid", baseType: .UINT16, value: Float64(UInt16.min) - 1, expectedValue: Float64(BaseType.UINT16.invalidValue() as UInt16)),
        ] as [(String, BaseType, Float64, Float64)]
        
        assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: testCases)
    }
    
    func test_correctRangeAndType_whenTypeIs4ByteInteger() {
        let testCases = [
            (title: "SInt32 Valid", baseType: .SINT32, value: -5, expectedValue: -5),
            (title: "SInt32 Max Invalid", baseType: .SINT32, value: Float64(Int32.max) + 1, expectedValue: Float64(BaseType.SINT32.invalidValue() as Int32)),
            (title: "SInt32 Min Invalid", baseType: .SINT32, value: Float64(Int32.min) - 1, expectedValue: Float64(BaseType.SINT32.invalidValue() as Int32)),
            (title: "UInt32 Valid", baseType: .UINT32, value: 10, expectedValue: 10),
            (title: "UInt32 Max Invalid", baseType: .UINT32, value: Float64(UInt32.max) + 1, expectedValue: Float64(BaseType.UINT32.invalidValue() as UInt32)),
            (title: "UInt32 Min Invalid", baseType: .UINT32, value: Float64(UInt32.min) - 1, expectedValue: Float64(BaseType.UINT32.invalidValue() as UInt32)),
        ] as [(String, BaseType, Float64, Float64)]
        
        assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: testCases)
    }
    
    func test_correctRangeAndType_whenTypeIs8ByteInteger() {
        let testCases = [
            (title: "UInt64 Valid", baseType: .UINT64, value: 10, expectedValue: 10),
            (title: "UInt64 Max Invalid", baseType: .UINT64, value: Float64(fitValue: UInt64.max).nextUp, expectedValue: Float64(BaseType.UINT64.invalidValue() as UInt64)),
            (title: "UInt64 Min Invalid", baseType: .UINT64, value: Float64(fitValue: UInt64.min).nextDown, expectedValue: Float64(BaseType.UINT64.invalidValue() as UInt64)),
            (title: "UInt64 Min Invalid NaN", baseType: .UINT64, value: Float64.nan, expectedValue: Float64(BaseType.UINT64.invalidValue() as UInt64)),
            (title: "SInt64 Valid", baseType: .SINT64, value: -10, expectedValue: -10),
            (title: "SInt64 Max Invalid", baseType: .SINT64, value: Float64(fitValue: Int64.max).nextUp, expectedValue: Float64(BaseType.SINT64.invalidValue() as Int64)),
            (title: "SInt64 Min Invalid", baseType: .SINT64, value: Float64(fitValue: Int64.min).nextDown, expectedValue: Float64(BaseType.SINT64.invalidValue() as Int64)),
            (title: "SInt64 Min Invalid NaN", baseType: .SINT64, value: Float64.nan, expectedValue: Float64(Int64.max)),
            (title: "UInt64Z Valid", baseType: .UINT64Z, value: 0x01, expectedValue: 0x01),
            (title: "UInt64Z Max Invalid", baseType: .UINT64Z, value: Float64(fitValue: UInt64.max).nextUp, expectedValue: Float64(BaseType.UINT64Z.invalidValue() as UInt64)),
            (title: "UInt64Z Min Invalid", baseType: .UINT64Z, value: Float64(fitValue: UInt64.min).nextDown, expectedValue: Float64(BaseType.UINT64Z.invalidValue() as UInt64)),
            (title: "UInt64Z Min Invalid NaN", baseType: .UINT64Z, value: Float64.nan, expectedValue: Float64(BaseType.UINT64Z.invalidValue() as UInt64)),
        ] as [(String, BaseType, Float64, Float64)]
        
        assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: testCases)
    }
    
    func test_correctRangeAndType_whenTypeIsZ() {
        let testCases = [
            (title: "UInt8Z Valid", baseType: .UINT8Z, value: 0x01, expectedValue: 0x01),
            (title: "UInt8Z Max Invalid", baseType: .UINT8Z, value: Float64(UInt8.max) + 1, expectedValue: Float64(BaseType.UINT8Z.invalidValue() as UInt8)),
            (title: "UInt8Z Min Invalid", baseType: .UINT8Z, value: Float64(UInt8.min) - 1, expectedValue: Float64(BaseType.UINT8Z.invalidValue() as UInt8)),
            (title: "UInt16Z Valid", baseType: .UINT16Z, value: 0x01, expectedValue: 0x01),
            (title: "UInt16Z Max Invalid", baseType: .UINT16Z, value: Float64(UInt16.max) + 1, expectedValue: Float64(BaseType.UINT16Z.invalidValue() as UInt16)),
            (title: "UInt16Z Min Invalid", baseType: .UINT16Z, value: Float64(UInt16.min) - 1, expectedValue: Float64(BaseType.UINT16Z.invalidValue() as UInt16)),
            (title: "UInt32Z Valid", baseType: .UINT32Z, value: 0x01, expectedValue: 0x01),
            (title: "UInt32Z Max Invalid", baseType: .UINT32Z, value: Float64(UInt32.max) + 1, expectedValue: Float64(BaseType.UINT32Z.invalidValue() as UInt32)),
            (title: "UInt32Z Min Invalid", baseType: .UINT32Z, value: Float64(UInt32.min) - 1, expectedValue: Float64(BaseType.UINT32Z.invalidValue() as UInt32)),
        ] as [(String, BaseType, Float64, Float64)]
        
        assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: testCases)
    }
    
    func test_correctRangeAndType_whenTypeIsFloatOrDouble() {
        let testCases = [
            (title: "Float32 Valid", baseType: .FLOAT32, value: -10, expectedValue: -10),
            (title: "Float32 Min Invalid NaN", baseType: .FLOAT32, value: Float64(fitValue: Float32.nan), expectedValue: Float64(fitValue: BaseType.FLOAT32.invalidValue() as Float32)),
            (title: "Float32 Min Invalid Infinity", baseType: .FLOAT32, value: Float64(fitValue: Float32.infinity), expectedValue:  Float64(fitValue: BaseType.FLOAT32.invalidValue() as Float32)),
            (title: "Float64 Valid", baseType: .FLOAT64, value: -10, expectedValue: -10),
            (title: "Float64 Min Invalid NaN", baseType: .FLOAT64, value: Float64(Float64.nan), expectedValue:  Float64(fitValue: BaseType.FLOAT64.invalidValue() as Float64)),
            (title: "Float64 Min Invalid Infinity", baseType: .FLOAT64, value: Float64(fitValue: Float64.infinity), expectedValue:  Float64(fitValue: BaseType.FLOAT64.invalidValue() as Float64)),
        ] as [(String, BaseType, Float64, Float64)]
        
        assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: testCases)
    }
    
    func test_correctRangeAndType_whenTypeIsStringAndInputIsString_returnsString() throws {
        let baseType = BaseType.STRING
        let string = "Test String"
        
        XCTAssertEqual(baseType.correctRangeAndType(string) as? String, string)
    }
    
    func test_correctRangeAndType_whenTypeIsStringAndInputIsFloat_returnsString() throws {
        let baseType = BaseType.STRING
        let stringFloat: Float32 = 32.0
        
        XCTAssertEqual(baseType.correctRangeAndType(stringFloat) as? String, "32.0")
    }
    
    func assertCorrectRangeAndTypeIsExpectedValueForAllTestCases(testCases: [(String, BaseType, Float64, Float64)]) {
        for (title, baseType, value, expectedValue) in testCases {
            XCTContext.runActivity(named: title) { activity in
                
                let result = Float64(fitValue: baseType.correctRangeAndType(value))
            
                if (expectedValue.isNaN) {
                    XCTAssertTrue(result.isNaN)
                }
                else {
                    XCTAssertEqual(result, expectedValue)
                }
            }
        }
    }

    func test_baseTypeFromAnyValue_returnsCorrectBaseType() throws {
        let testCases = [
            (title: "UInt8", value: UInt8.max, expected: .UINT8),
            (title: "UInt16", value: UInt16.max, expected: .UINT16),
            (title: "UInt32", value: UInt32.max, expected: .UINT32),
            (title: "UInt64", value: UInt64.max, expected: .UINT64),
            (title: "Int8", value: Int8.max, expected: .SINT8),
            (title: "Int16", value: Int16.max, expected: .SINT16),
            (title: "Int32", value: Int32.max, expected: .SINT32),
            (title: "Int64", value: Int64.max, expected: .SINT64),
            (title: "UInt8Z", value: UInt8.max, expected: .UINT8),
            (title: "UInt16Z", value: UInt16.max, expected: .UINT16),
            (title: "UInt32Z", value: UInt32.max, expected: .UINT32),
            (title: "UInt64Z", value: UInt64.max, expected: .UINT64),
            (title: "Float32", value: Float32.greatestFiniteMagnitude, expected: .FLOAT32),
            (title: "Float64", value: Float64.greatestFiniteMagnitude, expected: .FLOAT64),
            (title: "String", value: "Test", expected: .STRING),
            (title: "Bool", value: true, expected: .UINT8),
        ] as [(String, Any, BaseType)]
        
        for (title, value, expected) in testCases {
            XCTContext.runActivity(named: title) { activity in
                XCTAssertEqual(BaseType.from(value), expected)
            }
        }
    }

    func test_baseTypeFromUnsupportedType_returnsNil() {
        XCTAssertNil(BaseType.from(Decimal()))
        XCTAssertNil(BaseType.from(Int.max))
        XCTAssertNil(BaseType.from(UInt.max))
    }
}
