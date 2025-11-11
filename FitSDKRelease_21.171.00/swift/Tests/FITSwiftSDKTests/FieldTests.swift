/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import XCTest
@testable import FITSwiftSDK

func createTestFieldWithSingleValue(type: BaseType, value: Any?) throws -> Field {
    let field = Factory.createDefaultField(fieldNum: 0, baseType: type)
    try field.setValue(value: value)
    
    return field
}

final class FieldTests: XCTestCase {
    
    // MARK: Get and Set Value Tests
    func test_setValue_whenValueIsBool_setsValueTo0Or1() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT8)
        
        try field.setValue(value: true)
        XCTAssertEqual(field.getValue() as! UInt8, 1)
        
        try field.setValue(value: false)
        XCTAssertEqual(field.getValue() as! UInt8, 0)
    }
    
    func test_setFieldValue_whenBaseTypeIsStringAndPassedValueIsNumber_valueIsConvertedToAString() throws {
        let fileIdMesg = FileIdMesg.createFileIdMesg()
        
        try fileIdMesg.setFieldValue(fieldNum: FileIdMesg.productNameFieldNum, value: 1234)
        XCTAssertEqual(fileIdMesg.getProductName(), "1234")
    }

    func test_getAndSetValue_whenValuesAndBaseTypeAreSigned_returnsSameSignedValue() throws {
        let number = 100
        
        let testCases = [
            (title: "Int8 Negative Returns Negative", baseType: .SINT8, Int8.self, value: -number, expected: Int8(-number)),
            (title: "Int16 Negative Returns Negative", baseType: .SINT16, Int16.self, value: -number, expected: Int16(-number)),
            (title: "Int32 Negative Returns Negative", baseType: .SINT32, Int32.self, value: -number, expected: Int32(-number)),
            (title: "Int64 Negative Returns Negative", baseType: .SINT64, Int64.self, value: -number, expected: Int64(-number)),
            (title: "Float32 Negative Returns Negative", baseType: .FLOAT32, Float32.self, value: -number, expected: Float32(-number)),
            (title: "Float64 Negative Returns Negative", baseType: .FLOAT64, Float64.self, value: -number, expected: Float64(-number)),
            (title: "Int8 Positive Returns Positive", baseType: .SINT8, Int8.self, value: number, expected: Int8(number)),
            (title: "Int16 Positive Returns Positive", baseType: .SINT16, Int16.self, value: number, expected: Int16(number)),
            (title: "Int32 Positive Returns Positive", baseType: .SINT32, Int32.self, value: number, expected: Int32(number)),
            (title: "Int64 Positive Returns Positive", baseType: .SINT64, Int64.self, value: number, expected: Int64(number)),
            (title: "Float32 Positive Returns Positive", baseType: .FLOAT32, Float32.self, value: number, expected: Float32(number)),
            (title: "Float64 Positive Returns Positive", baseType: .FLOAT64, Float64.self, value: number, expected: Float64(number)),
        ] as [(String, BaseType, any Equatable.Type, Any, Any)]
        
        for (title, baseType, swiftType, value, expected) in testCases {
            try XCTContext.runActivity(named: title) { activity in
                let field = Factory.createDefaultField(fieldNum: 0, baseType: baseType)
                try field.setValue(value: value)
                
                try assertValueAndExpectedValueEqual(swiftType: swiftType, value: field.getValue()!, expected: expected)
            }
        }
    }

    func test_getAndSetValue_whenValueIsInvalidValue_returnsNil() throws {
        let testCases = [
            (title: "Enum", baseType: .ENUM, value: Data([0xFF]), swiftType: UInt8.self),
            (title: "Byte", baseType: .BYTE, value: Data([0xFF]), swiftType: UInt8.self),
            (title: "UInt8", baseType: .UINT8, value: Data([0xFF]), swiftType: UInt8.self),
            (title: "UInt8Z", baseType: .UINT8Z, value: Data([0x00]), swiftType: UInt8.self),
            (title: "SInt8", baseType: .SINT8, value: Data([0x7F]), swiftType: Int8.self),
            (title: "UInt16", baseType: .UINT16, value: Data([0xFF, 0xFF]), swiftType: UInt16.self),
            (title: "UInt16Z", baseType: .UINT16Z, Data([0x00, 0x00]), swiftType: UInt16.self),
            (title: "SInt16", baseType: .SINT16, value: Data([0xFF, 0x7F]), swiftType: Int16.self),
            (title: "UInt32", baseType: .UINT32, value: Data([0xFF, 0xFF, 0xFF, 0xFF]), swiftType: UInt32.self),
            (title: "UInt32Z", baseType: .UINT32Z, value: Data([0x00, 0x00, 0x00, 0x00]), swiftType: UInt32.self),
            (title: "SInt32", baseType: .SINT32, value: Data([0xFF, 0xFF, 0xFF, 0x7F]), swiftType: Int32.self),
            (title: "UInt64", baseType: .UINT64, value: Data([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]), swiftType: UInt64.self),
            (title: "UInt64Z", baseType: .UINT64Z, value: Data([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]), swiftType: UInt64.self),
            (title: "SInt64", baseType: .SINT64, value: Data([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F]), swiftType: Int64.self),
            (title: "Float32", baseType: .FLOAT32, value: Data([0xFF, 0xFF, 0xFF, 0xFF]), swiftType: Float32.self),
            (title: "Float64", baseType: .FLOAT64, value: Data([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]), swiftType: Float64.self),
        ] as [(String, BaseType, Data, any Equatable.Type)]
        
        for (title, baseType, value, swiftType) in testCases {
            try XCTContext.runActivity(named: title) { activity in
                try assertFieldSetToInvalidValueAndType(swiftType: swiftType.self, value: value, baseType: baseType)
            }
        }
    }
    
    // MARK: addRawValue Tests
    func test_addRawValue_whenFieldHasScaleAndOffset_doesNotApplyScaleOrOffset() throws {
        let rawValue: UInt8 = 100
        let scaledAndOffsetValueExpected = (Float64(rawValue) / 2) - 5
        
        let field = Field(name: "ScaleAndOffset", num: 0, type: BaseType.UINT8.rawValue, scale: 2, offset: 5, units: "", accumulated: false)
        try field.addRawValue(UInt8(100))
                
        XCTAssertEqual(field.getValue() as! Float64, scaledAndOffsetValueExpected)
    }
    
    func test_addRawValue_whenCalledMultipleTimes_appendsToValues() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: .UINT8)
        
        try field.addRawValue(0)
        try field.addRawValue(1)
        try field.addRawValue(2)
        
        XCTAssertEqual(field.getValue(index: 0) as! UInt8, 0)
        XCTAssertEqual(field.getValue(index: 1) as! UInt8, 1)
        XCTAssertEqual(field.getValue(index: 2) as! UInt8, 2)
    }
    
    func test_addRawValue_correctsRangeAndValue() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: .UINT8)
        
        try field.addRawValue(0)
        try field.addRawValue(UInt64.max)
        try field.addRawValue(2)
        
        XCTAssertEqual(field.getValue(index: 0) as! UInt8, 0)
        XCTAssertNil(field.getValue(index: 1))
        XCTAssertEqual(field.getValue(index: 2) as! UInt8, 2)
    }
    
    // MARK: Field Size Overflow Tests
    func test_setValue_whenTypeIsUInt8AndArraySizeExceeds255_throwsError() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: .UINT8)
        
        XCTAssertNoThrow(try field.setValue(index: 0, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 0, value: nil, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 254, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 254, value: nil, subField: nil))
        
        XCTAssertThrowsError(try field.setValue(index: 255, value: nil, subField: nil)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?
                
                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
        }
    }
    
    func testArrayBoundtest_setValue_whenTypeIsUInt16AndArraySizeExceeds126_throwsError() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT16)
        
        XCTAssertNoThrow(try field.setValue(index: 0, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 0, value: nil, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 126, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 126, value: nil, subField: nil))
        
        XCTAssertThrowsError(try field.setValue(index: 127, value: 0, subField: nil)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?
                
                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
        }
    }
    
    func test_setValue_whenTypeIsUInt32AndArraySizeExceeds62_throwsError() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT32)
        
        XCTAssertNoThrow(try field.setValue(index: 0, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 0, value: nil, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 62, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 62, value: nil, subField: nil))
        
        XCTAssertThrowsError(try field.setValue(index: 63, value: 0, subField: nil)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?
                
                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
        }
    }
    
    func test_setValue_whenTypeIsUInt64AndArraySizeExceeds30_throwsError() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT64)
        
        XCTAssertNoThrow(try field.setValue(index: 0, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 0, value: nil, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 30, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 30, value: nil, subField: nil))
        
        XCTAssertThrowsError(try field.setValue(index: 31, value: 0, subField: nil)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?
                
                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
        }
    }

    func test_addRawValue_whenTypeIsUInt8AndArraySizeExceeds255_throwsError() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT8)
        
        XCTAssertNoThrow(try field.addRawValue(UInt8(0)))
        XCTAssertNoThrow(try field.setValue(index: 253, value: 0, subField: nil))
        XCTAssertNoThrow(try field.setValue(index: 253, value: nil, subField: nil))
        XCTAssertNoThrow(try field.addRawValue(UInt8(0)))
        
        XCTAssertThrowsError(try field.addRawValue(UInt8(0))) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?
                
                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
        }
    }

    // MARK: Value Rounding Tests
    func test_setValue_whenFieldIsFloatingPoint_valueIsNotRounded() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: .FLOAT32)
        let value: Float32 = 123.678

        try field.setValue(value: value)

        XCTAssertEqual(field.getValue() as! Float32, value)
    }

    func test_setValue_whenFieldIsIntegerWithNoScale_valueIsTruncated() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: .UINT16)
        let value: Float32 = 123.678

        try field.setValue(value: value)

        XCTAssertEqual(field.getValue() as! UInt16, 123)
    }

    func test_setValue_whenFieldIsIntegerWithScale_valueIsRounded() throws {
        let field = Field(name: "scaled", num: 0, type: BaseType.UINT8.rawValue, scale: 2, offset: 0, units: "", accumulated: false)
        let value: Float32 = 62.378

        try field.setValue(value: value)

        // Value gets scaled to 124.7 and rounded to 125, and should be 62.5 when retrieved
        XCTAssertEqual(field.getValue() as! Float64, 62.5)
    }
    
    // MARK: Scale and Offset Tests
    func test_setValue_whenFieldHasNoScaleOrOffset_storedValueEqualsInputValue() throws {
        let field = Field(name: "noScaleOrOffset", num: 250, type: BaseType.UINT16.rawValue, scale: 1, offset: 0, units: "m", accumulated: false)
        let value: UInt16 = 100
        
        try field.setValue(value: value)
                
        XCTAssertNotNil(field.getValue() as? UInt16)
        XCTAssertEqual(field.getValue() as! UInt16, value)
        
        // Verify that the stored value is also the same as there is no scale or offset
        let storedValue = UInt16(fitValue: field.values[0])
        
        XCTAssertEqual(storedValue, value)
    }
    
    func test_setValue_whenFieldHasOffset_getValueReturnsFloat64() throws {
        let field = Field(name: "offsetOnly", num: 250, type: BaseType.UINT16.rawValue, scale: 1, offset: 10, units: "m", accumulated: false)
        let value: Float64 = 50
        
        try field.setValue(value: value)

        XCTAssertNotNil(field.getValue() as? Float64)
        XCTAssertEqual(field.getValue() as! Float64, value)
        
        // Verify that the stored value has the scale and offset applied and is the base type (not Float64)
        let storedValue = UInt16(fitValue: field.values[0])
        let expectedStoredValue: UInt16 = 50 + 10
        
        XCTAssertEqual(storedValue, expectedStoredValue)
    }
    
    func test_setValue_whenFieldHasScaleAndOffset_getValueReturnsFloat64() throws {
        let field = Field(name: "scaleAndOffset", num: 250, type: BaseType.UINT16.rawValue, scale: 2, offset: 10, units: "m", accumulated: false)
        let value: Float64 = 50
        
        try field.setValue(value: value)

        XCTAssertNotNil(field.getValue() as? Float64)
        XCTAssertEqual(field.getValue() as! Float64, value)
        
        // Verify that the stored value has the scale and offset applied and is the base type (not Float64)
        let storedValue = UInt16(fitValue: field.values[0])
        let expectedStoredValue: UInt16 = (50 + 10) * 2
        
        XCTAssertEqual(storedValue, expectedStoredValue)
    }
    
    func test_setValue_whenFieldIsArray_scaleAndOffsetIsApplied() throws {
        let field = Field(name: "arrayField", num: 250, type: BaseType.UINT16.rawValue, scale: 2, offset: 10, units: "m", accumulated: false)
        let value1: Float64 = 10
        let value2: Float64 = 20
        
        try field.setValue(index: 0, value: value1)
        try field.setValue(index: 1, value: value2)
        
        XCTAssertEqual(field.toArray(), [10.0, 20.0])
    }
    
    // MARK: Equatable Tests
    func test_equatable_whenPropertiesAreTheSameOrDifferent_returnsExpected() throws {
        let field = Field(name: "Field", num: 0, type: 0, scale: 0, offset: 0, units: "", accumulated: false)
        
        let testCases = [
            (title: "Identical Field", value: Field(name: "Field", num: 0, type: 0, scale: 0, offset: 0, units: "", accumulated: false), expected: true),
            (title: "Field with Different Name", value: Field(name: "Field1", num: 0, type: 0, scale: 0, offset: 0, units: "", accumulated: false), expected: false),
            (title: "Field with Different Field num", value: Field(name: "Field", num: 1, type: 0, scale: 0, offset: 0, units: "", accumulated: false), expected: false),
            (title: "Field with Different Type", value: Field(name: "Field", num: 0, type: 1, scale: 0, offset: 0, units: "", accumulated: false), expected: false),
            (title: "Field with Different Scale", value: Field(name: "Field", num: 0, type: 0, scale: 10, offset: 0, units: "", accumulated: false), expected: false),
            (title: "Field with Different Offset", value: Field(name: "Field", num: 0, type: 0, scale: 0, offset: 100, units: "", accumulated: false), expected: false),
            (title: "Field with Different Units", value: Field(name: "Field", num: 0, type: 0, scale: 0, offset: 0, units: "m", accumulated: false), expected: false),
        ] as [(String, Field, Bool)]
        
        for (title, value, expected) in testCases {
            XCTContext.runActivity(named: title) { activity in
                XCTAssertEqual((field == value), expected)
            }
        }
    }
    
    func test_equatable_whenFieldContainsSingleValue_equatesOnValueAndIsExpected() throws {
        let testCases = [
            (title: "UInt8 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT8, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT8, value: 100), expected: true),
            (title: "UInt8 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT8, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT8, value: 101), expected: false),
            (title: "Enum matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.ENUM, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.ENUM, value: 100), expected: true),
            (title: "Enum different value", lhs: try createTestFieldWithSingleValue(type: BaseType.ENUM, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.ENUM, value: 101), expected: false),
            (title: "UInt8z matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT8Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT8Z, value: 100), expected: true),
            (title: "UInt8z different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT8Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT8Z, value: 101), expected: false),
            (title: "Byte matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.BYTE, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.BYTE, value: 100), expected: true),
            (title: "Byte different value", lhs: try createTestFieldWithSingleValue(type: BaseType.BYTE, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.BYTE, value: 101), expected: false),
            (title: "SInt8 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT8, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT8, value: 100), expected: true),
            (title: "SInt8 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT8, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT8, value: 101), expected: false),
            (title: "SInt16 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT16, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT16, value: 100), expected: true),
            (title: "SInt16 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT16, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT16, value: 101), expected: false),
            (title: "UInt16 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT16, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT16, value: 100), expected: true),
            (title: "UInt16 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT16, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT16, value: 101), expected: false),
            (title: "UInt16Z matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT16Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT16Z, value: 100), expected: true),
            (title: "UInt16Z different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT16Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT16Z, value: 101), expected: false),
            (title: "SInt32 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT32, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT32, value: 100), expected: true),
            (title: "SInt32 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT32, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT32, value: 101), expected: false),
            (title: "UInt32 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT32, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT32, value: 100), expected: true),
            (title: "UInt32 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT32, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT32, value: 101), expected: false),
            (title: "UInt32Z matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT32Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT32Z, value: 100), expected: true),
            (title: "UInt32Z different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT32Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT32Z, value: 101), expected: false),
            (title: "Float32 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT32, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT32, value: 100), expected: true),
            (title: "Float32 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT32, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT32, value: 101), expected: false),
            (title: "Float64 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT64, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT64, value: 100), expected: true),
            (title: "Float64 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT64, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.FLOAT64, value: 101), expected: false),
            (title: "SInt64 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT64, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT64, value: 100), expected: true),
            (title: "SInt64 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.SINT64, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.SINT64, value: 101), expected: false),
            (title: "UInt64 matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT64, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT64, value: 100), expected: true),
            (title: "UInt64 different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT64, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT64, value: 101), expected: false),
            (title: "UInt64Z matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT64Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT64Z, value: 100), expected: true),
            (title: "UInt64Z different value", lhs: try createTestFieldWithSingleValue(type: BaseType.UINT64Z, value: 100), rhs: try createTestFieldWithSingleValue(type: BaseType.UINT64Z, value: 101), expected: false),
            (title: "String matching value", lhs: try createTestFieldWithSingleValue(type: BaseType.STRING, value: "value"), rhs: try createTestFieldWithSingleValue(type: BaseType.STRING, value: "value"), expected: true),
            (title: "String different value", lhs: try createTestFieldWithSingleValue(type: BaseType.STRING, value: "value"), rhs: try createTestFieldWithSingleValue(type: BaseType.STRING, value: "different value"), expected: false),
            

        ] as [(String, Field, Field, Bool)]
        
        for (title, lhs, rhs, expected) in testCases {
            XCTContext.runActivity(named: title) { activity in
                XCTAssertEqual((lhs == rhs), expected)
            }
        }
    }
    
    func assertFieldSetToInvalidValueAndType<T: Equatable>(swiftType: T.Type, value: Data, baseType: BaseType) throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: baseType)
        
        XCTAssertNoThrow(try field.setValue(value: value.withUnsafeBytes { $0.load(as: swiftType) }))
        
        // The underlying value should be equal to the invalid value
        if(baseType == BaseType.FLOAT32 || baseType == BaseType.FLOAT64) {
            XCTAssertTrue(Float64(fitValue: field.values[0]).isNaN)
        }
        else {
            XCTAssertEqual(field.values[0] as? T, baseType.invalidValue() as T)
        }
        
        // Getting the invalid value should return nil
        XCTAssertNil(field.getValue())
    }

    func assertValueAndExpectedValueEqual<T: Equatable>(swiftType: T.Type, value: Any, expected: Any) throws {
        XCTAssertEqual(value as! T, expected as! T)
    }
    
    // MARK: SubField Tests
    func test_setFileIdMesgProductSubfield_withMissingManufacturerReferenceMessage_throwsError() throws {
        let fileIdMesg = FileIdMesg()
        XCTAssertThrowsError(try fileIdMesg.setGarminProduct(GarminProduct.fenix8))
    }
    
    func test_setFileIdMesgProductSubfield_withIncompatableManufacturerType_throwsError() throws {
        let fileIdMesg = FileIdMesg()
        try fileIdMesg.setManufacturer(Manufacturer.development)
        XCTAssertThrowsError(try fileIdMesg.setGarminProduct(GarminProduct.fenix8))
    }
    
    func test_setFileIdMesgProductSubfield_withCorrectRefFieldValues_returnsExpectedSubfieldValues() throws {
        let fileIdMesg = FileIdMesg()
        try fileIdMesg.setManufacturer(Manufacturer.garmin)
        try fileIdMesg.setGarminProduct(GarminProduct.fenix8)

        let productField = fileIdMesg.getField(fieldName: "Product")
        let faveroProductSubField = productField?.getSubField(subFieldName: "FaveroProduct")
        let garminProductSubField = productField?.getSubField(subFieldName: "GarminProduct")
        
        XCTAssertEqual(fileIdMesg.getManufacturer(), Manufacturer.garmin)
        XCTAssertFalse(try faveroProductSubField!.canMesgSupport(mesg: fileIdMesg))
        XCTAssertTrue(try garminProductSubField!.canMesgSupport(mesg: fileIdMesg))
        XCTAssertEqual(try fileIdMesg.getGarminProduct(), GarminProduct.fenix8)
        XCTAssertNil(try fileIdMesg.getFaveroProduct())
        XCTAssertEqual(fileIdMesg.getProduct(), 4536)
    }
    
    func test_getSubfieldValue_whenSubfieldHasScale_scaleIsApplied() throws {
        let workoutStepMesg = WorkoutStepMesg()
        try workoutStepMesg.setDurationType(WktStepDuration.time)
        try workoutStepMesg.setDurationTime(1)
        
        XCTAssertEqual(try workoutStepMesg.getDurationTime(),1)
        XCTAssertEqual(workoutStepMesg.getDurationValue(),1000)
    }
    
    func test_setSubfieldValue_whenInputIsFloat_scaleAndOffsetIsApplied() throws {
        let workoutStepMesg = WorkoutStepMesg()
        try workoutStepMesg.setDurationType(WktStepDuration.time)
        try workoutStepMesg.setDurationTime(0.01)

        XCTAssertEqual(try workoutStepMesg.getDurationTime(),0.01)
        XCTAssertEqual(workoutStepMesg.getDurationValue(),10)
    }
    
    func test_getActiveSubFieldIndex_whenSubFieldExists_returnsExpectedValue() throws {
        let workoutStepMesg = WorkoutStepMesg()
        try workoutStepMesg.setDurationType(WktStepDuration.distance)
        try workoutStepMesg.setDurationDistance(1)
        
        let activeSubFieldValue = try workoutStepMesg.getFieldValue(fieldNum: WorkoutStepMesg.durationValueFieldNum, index: 0, subFieldIndex: FIT.SUBFIELD_INDEX.ACTIVE_SUBFIELD) as? Float64
        XCTAssertEqual(activeSubFieldValue, 1)
        
        XCTAssertEqual(try workoutStepMesg.getActiveSubFieldIndex(fieldNum: WorkoutStepMesg.durationValueFieldNum), 1)
        
        let durationValueField = workoutStepMesg.getField(fieldName: "DurationValue")
        let durationDistanceSubField = durationValueField?.getSubField(subFieldName: "DurationDistance")
        XCTAssertTrue(try durationDistanceSubField!.canMesgSupport(mesg: workoutStepMesg))
        
        XCTAssertNil(try workoutStepMesg.getDurationTime())
        XCTAssertEqual(try workoutStepMesg.getDurationDistance(), 1)
        XCTAssertEqual(workoutStepMesg.getDurationValue(), 100)
        XCTAssertEqual(activeSubFieldValue, try workoutStepMesg.getDurationDistance())
    }
    
    
    func test_getSubFieldValue_whenincompatibleTypeActiveSubField_throwsError() throws {
        let workoutStepMesg = WorkoutStepMesg()
        try workoutStepMesg.setDurationType(WktStepDuration.time)
        XCTAssertThrowsError(try workoutStepMesg.setDurationDistance(1))
    }
    
    func test_setActiveSubField_withoutType_throwsError() throws {
        let workoutStepMesg = WorkoutStepMesg()
        XCTAssertThrowsError(try workoutStepMesg.setDurationDistance(1))
    }
    
    func test_getFieldAndSubfieldTypeNameAndUnits_fromFieldWithSubField_returnsSubFieldExpectedValuesFromProfile() throws {
        let workoutStepMesg = WorkoutStepMesg()
        try workoutStepMesg.setDurationType(WktStepDuration.time)
        try workoutStepMesg.setDurationTime(1)
        
        let durationValueField = workoutStepMesg.getField(fieldName: "DurationValue")
        
        XCTAssertEqual(durationValueField!.getName(), "DurationValue")
        XCTAssertEqual(durationValueField!.getName(subFieldIndex: 0), "DurationTime")
        XCTAssertEqual(durationValueField!.getName(subFieldName: "DurationTime"), "DurationTime")
        
        XCTAssertEqual(durationValueField!.getType(), BaseType.UINT32.rawValue)
        XCTAssertEqual(durationValueField!.getType(subFieldIndex: 0), BaseType.UINT32.rawValue)
        XCTAssertEqual(durationValueField!.getType(subFieldName: "DurationTime"), BaseType.UINT32.rawValue)
        
        XCTAssertEqual(durationValueField!.getUnits(), "")
        XCTAssertEqual(durationValueField!.getUnits(subFieldIndex: 0), "s")
        XCTAssertEqual(durationValueField!.getUnits(subFieldName: "DurationTime"), "s")
    }
    
    // MARK: Array Field Tests
    func test_byteArrayFieldRead_whenAllValuesAreValid_byteArrayFieldIsValid() throws {
        let byteArrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.BYTE)

        let stream = FITSwiftSDK.InputStream(data: Data([0x01, 0x02, 0x03, 0x04]))

        try byteArrayField.read(stream: stream, size: 4)

        XCTAssertTrue(byteArrayField.hasValues)
    }

    func test_byteArrayFieldRead_whenAnyValuesAreInvalid_byteArrayFieldIsValid() throws {
        let testCases = [
            (title: "First element is invalid 0111", data: Data([0xFF, 0x02, 0x03, 0x04])),
            (title: "Second element is invalid 1011", data: Data([0x01, 0xFF, 0x03, 0x04])),
            (title: "Third element is invalid 1101", data: Data([0x01, 0x02, 0xFF, 0x04])),
            (title: "Last element is invalid 1110", data: Data([0x01, 0x02, 0x03, 0xFF])),
            (title: "First and last elements are invalid 0110", data: Data([0xFF, 0x02, 0x03, 0xFF])),
            (title: "Middle elements are invalid 1001", data: Data([0x01, 0xFF, 0xFF, 0x04])),
            (title: "Odd indexes are invalid 1010", data: Data([0x01, 0xFF, 0x03, 0xFF])),
            (title: "Even indexes are invalid 0101", data: Data([0xFF, 0x02, 0xFF, 0x04])),
            (title: "All but first element are invalid 1000", data: Data([0x01, 0xFF, 0xFF, 0xFF])),
            (title: "All but second element are invalid 0100", data: Data([0xFF, 0x02, 0xFF, 0xFF])),
            (title: "All but third element are invalid 0010", data: Data([0xFF, 0xFF, 0x03, 0xFF])),
            (title: "All but forth element are invalid 0001", data: Data([0xFF, 0xFF, 0xFF, 0x04])),
            (title: "First two elements are invalid 0011", data: Data([0xFF, 0xFF, 0x03, 0x04])),
            (title: "Last two elements are invalid 1100", data: Data([0x01, 0x02, 0xFF, 0xFF])),

        ] as [(String, Data)]

        for (title, data) in testCases {
            try XCTContext.runActivity(named: title) { activity in
                let byteArrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.BYTE)

                let stream = FITSwiftSDK.InputStream(data: data)

                try byteArrayField.read(stream: stream, size: 4)

                XCTAssertTrue(byteArrayField.hasValues)
            }
        }
    }

    func test_byteArrayFieldRead_whenAllValuesAreInvalid_byteArrayFieldIsInvalid() throws {
        let byteArrayField = Field(name: "array field", num: 0, type: BaseType.BYTE.rawValue , scale: 1.0, offset: 0, units: "", accumulated: false)

        let stream = FITSwiftSDK.InputStream(data: Data([0xFF, 0xFF, 0xFF, 0xFF]))

        try byteArrayField.read(stream: stream, size: 4)

        XCTAssertFalse(byteArrayField.hasValues)
    }

    func test_getValue_whenGivenIndex_returnsFieldValueAtIndex() throws {
        let arrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.SINT32)
        
        try arrayField.setValue(index: 0, value: 0)
        try arrayField.setValue(index: 1, value: 10)

        XCTAssertEqual(arrayField.getValue(index: 0) as! Int32, 0)
        XCTAssertEqual(arrayField.getValue(index: 1) as! Int32, 10)
        XCTAssertNil(arrayField.getValue(index: 2))
    }

    func test_toArray_returnsArrayOfFieldValues() throws {
        let arrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.SINT32)

        try arrayField.setValue(index: 0, value: 0)
        try arrayField.setValue(index: 1, value: 10)

        XCTAssertEqual(arrayField.toArray() as [Int32?], [0, 10])
    }

    func test_numValues_whenFieldHas0Values_returnsZero() throws {
        let arrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.SINT32)
        XCTAssertEqual(arrayField.numValues, 0)
    }

    func test_numValues_whenFieldHasValues_returnsNumberOfFieldValues() throws {
        let arrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.SINT32)

        try arrayField.setValue(index: 0, value: 0)
        try arrayField.setValue(index: 1, value: 10)

        XCTAssertEqual(arrayField.numValues, 2)
    }

    func test_getValue_whenFieldIsEmpty_returnsNil() throws {
        let arrayField = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.SINT32)
        XCTAssertNil(arrayField.getValue(index: 0))
    }
    
    // MARK: String Field Tests
    func test_read_whenFieldIsSingleByteStringWithNullTerminator_trimsTerminatorsAndReadsExpectedValue() throws {
        let data = Data([0x2E, 0x46, 0x49, 0x54, 0x00])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,1)
        XCTAssertEqual(field.getStringValue(), ".FIT")
    }
    
    func test_read_whenFieldIsSingleByteStringWithoutNullTerminator_readsExpectedValue() throws {
        let data = Data([0x2E, 0x46, 0x49, 0x54])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,1)
        XCTAssertEqual(field.getStringValue(), ".FIT")
    }
    
    func test_read_whenFieldIsSingleByteStringWithTrailingNullTerminators_trimsTerminatorsAndReadsExpectedValue() throws {
        let data = Data([0x2E, 0x46, 0x49, 0x54, 0x00, 0x00, 0x00, 0x00])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,1)
        XCTAssertEqual(field.getStringValue(), ".FIT")
    }
    
    func test_read_whenFieldIsArrayOfStringsWithNullTerminators_trimsTerminatorsAndReadsExpectedValues() throws {
        let data = Data([0x2E, 0x46, 0x49, 0x54, 0x00, 0x47, 0x61, 0x72, 0x6d, 0x69, 0x6e, 0x00])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,2)
        XCTAssertEqual(field.getStringValue(index: 0), ".FIT")
        XCTAssertEqual(field.getStringValue(index: 1), "Garmin")
    }
    
    func test_read_whenFieldIsSingleByteStringWithStartingNullTerminators_trimsTerminatorsAndReadsExpectedValue() throws {
        let data = Data([0x00, 0x00, 0x00, 0x00, 0x2E, 0x46, 0x49, 0x54])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,5)
        XCTAssertEqual(field.getStringValue(index: 0), nil)
        XCTAssertEqual(field.getStringValue(index: 1), nil)
        XCTAssertEqual(field.getStringValue(index: 2), nil)
        XCTAssertEqual(field.getStringValue(index: 3), nil)
        XCTAssertEqual(field.getStringValue(index: 4), ".FIT")
    }
    
    func test_read_whenFieldIsArrayOfStringsWithLeadingAndTrailingNullTerminators_onlyTrimsTrailingTerminators() throws {
        let data = Data([0x00, 0x00, 0x00, 0x00, 0x2E, 0x46, 0x49, 0x54, 0x00, 0x00, 0x00, 0x00])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,5)
        XCTAssertEqual(field.getStringValue(index: 0), nil)
        XCTAssertEqual(field.getStringValue(index: 1), nil)
        XCTAssertEqual(field.getStringValue(index: 2), nil)
        XCTAssertEqual(field.getStringValue(index: 3), nil)
        XCTAssertEqual(field.getStringValue(index: 4), ".FIT")
    }
    
    func test_read_whenFieldIsSingleByteStringWithMultibyteCharacters_mulitbyteCharactersAreRead() throws {
        let data = Data([0x61, 0xD1, 0x84, 0xE1, 0x90, 0x83, 0xF0, 0x9D, 0x95, 0xAB, 0x7A, 0x00])
        
        let field = try readStringFieldWithData(data)
        
        XCTAssertEqual(field.count,1)
        XCTAssertEqual(field.getStringValue(), "aфᐃ𝕫z")
    }
    
    func test_setValue_whenValueIsStringOfSingleByteCharacters_noError() throws {
        XCTAssertNoThrow(try setStringValue(index: 0, value: "Short String Of Single Byte Characters"))
    }
    
    func test_setValue_whenValueIsStringOfMultiByteCharacters_noError() throws {
        XCTAssertNoThrow(try setStringValue(index: 0, value: "这套动作由两组"))
    }
    
    func test_setValue_whenValueIsStringOfSingleByteCharactersAndExceeds255Bytes_noError() throws {
        // 255 Bytes + the null terminator = 256 = fail
        let value = "AS4EgyRNHimg4Pw3bUiFQwGyOttIQti8kHzPcfoUQ1kxi4PGVpwuE7MVlfnA0PjvIdWYn" +
        "L5yDX4LmULwXFTt8jGqfafPSoL3CXmYVGaTHuB1ILbjdVtPGPm0FQPyS6NVeJ97cBYI6PoVI7wmRnc7MLS903ckhJephd" +
        "Y1OdBKJ4YRWTmhrR712BSl59SEwDs6uLHLUvWnA6JE6aVPkN2LJbI11QAtKzXNORWcK2ggsWqtsAzxSsdGyXCs6qs6CDx"
        
        XCTAssertThrowsError(try setStringValue(index: 0, value: value)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?

                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
            
        }
    }
    
    func test_setValue_whenValueIsStringOfMultiByteCharactersAndExceeds255Bytes_noError() throws {
        // 255 Bytes + the null terminator = 256 = fail
        let value = "这套动作由两组 4 分钟的 Tabata 训练组成，中间休息 1 分钟。对于每组 Tabata 训练，在" +
        "训练的 20 秒内尽可能多地重复完成动作，休息 10 秒，然后重复动作总共 8 组。在 Tabata 训练中间，还有 1 分."
        
        XCTAssertThrowsError(try setStringValue(index: 0, value: value)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?
                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
            
        }
    }
    
    func test_setValue_whenFieldIsArrayOfStringsAndFieldSizeExceeds255Bytes_throwsSizeOverflowError() throws {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.STRING)
        
        // 254 Bytes + the null terminator = 255 = ok
        let value = "AS4EgyRNHimg4Pw3bUiFQwGyOttIQti8kHzPcfoUQ1kxi4PGVpwuE7MVlfnA0PjvIdWYn" +
        "L5yDX4LmULwXFTt8jGqfafPSoL3CXmYVGaTHuB1ILbjdVtPGPm0FQPyS6NVeJ97cBYI6PoVI7wmRnc7MLS903ckhJephd" +
        "Y1OdBKJ4YRWTmhrR712BSl59SEwDs6uLHLUvWnA6JE6aVPkN2LJbI11QAtKzXNORWcK2ggsWqtsAzxSsdGyXCs6qs6CD"
        
        XCTAssertNoThrow(try field.setValue(index: 0, value: value, subField: nil))
        
        // Even a blank string will have a null terminator, and in this case push the field size over 255 bytes
        XCTAssertThrowsError(try field.setValue(index: 1, value: "", subField: nil)) { (error) in
            switch error {
            case Field.FieldError.sizeOverflow(let size):
                // sizeOverflow is the correct error, but is the size correct?

                if(size != 256) {
                    XCTFail("Unexpected overflow size")
                }
            default:
                XCTFail("Unexpected error thrown")
            }
            
        }
    }
    
    // MARK: FieldBase Write Tests
    func test_write_emptyNilStringField_writesNullTerminator() throws {
        let outputStream = OutputStream()
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.STRING)
        
        field.values.append("")
        
        let fieldDefintition = FieldDefinition(field: field)
        
        XCTAssertNoThrow(field.write(outputStream: outputStream))
        XCTAssertEqual(outputStream[0], 0)
        XCTAssertEqual(Int(fieldDefintition.size), outputStream.count)
    }
    
    func test_write_ArrayValidValues_writes() throws {
        let outputStream = OutputStream()
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT8)
        
        let byteArray: [UInt8] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
        
        field.values = byteArray
        
        XCTAssertNoThrow(field.write(outputStream: outputStream))
        
        XCTAssertEqual(field.values.count, outputStream.count)
        for (index, value) in field.values.enumerated() {
            XCTAssertEqual(UInt8(fitValue: value), outputStream[index])
        }
    }
    
    func test_write_whenArrayContainsInvalid_continuesWritingPastInvalid() throws {
        let outputStream = OutputStream()
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.UINT8)
        
        let byteArray: [UInt8?] = [0, 1, 2, 3, 255, 5, 6, 7, 8, 9, 10]
        
        field.values = byteArray
        
        XCTAssertNoThrow(field.write(outputStream: outputStream))
        
        XCTAssertEqual(field.values.count, outputStream.count)
        for (index, value) in field.values.enumerated() {
            XCTAssertEqual(UInt8(fitValue: value), outputStream[index])
        }
    }
    
    func readStringFieldWithData(_ data: Data) throws -> Field {
        let stream = FITSwiftSDK.InputStream(data: data)
        
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.STRING)
        let fieldDefinition = FieldDefinition(num: field.num, size: UInt8(data.count), type: field.type)
        
        try field.read(stream: stream, size: fieldDefinition.size)
        
        return field
    }
    
    func setStringValue(index: Int, value: String) throws -> Field {
        let field = Factory.createDefaultField(fieldNum: 0, baseType: BaseType.STRING)
        try field.setValue(index: index, value: value, subField: nil)
        
        return field
    }
    
}
