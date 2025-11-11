/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import XCTest
@testable import FITSwiftSDK

func createTestDeveloperField(developerDataIndex: UInt8 = 0, fieldDefinitionNumber: UInt8 = 0, fitBaseType: FitBaseType = .float32, fieldName: String = "DevField", fieldIndex: Int = 0, units: String = "", scale: UInt8 = 0, offset: Int8 = 0) throws -> DeveloperField {
    let developerDataIdMesg = DeveloperDataIdMesg()
    try developerDataIdMesg.setDeveloperDataIndex(developerDataIndex)
    
    let fieldDescMesg = FieldDescriptionMesg()
    try fieldDescMesg.setDeveloperDataIndex(developerDataIndex)
    try fieldDescMesg.setFieldDefinitionNumber(fieldDefinitionNumber)
    try fieldDescMesg.setFitBaseTypeId(fitBaseType)
    try fieldDescMesg.setFieldName(index: fieldIndex, value: fieldName)
    try fieldDescMesg.setUnits(index: fieldIndex, value: units)
    try fieldDescMesg.setScale(scale)
    try fieldDescMesg.setOffset(offset)
    
    let developerFieldDefinition = DeveloperFieldDefinition(fieldDescriptionMesg: fieldDescMesg, developerDataIdMesg: developerDataIdMesg, size: 0)
    
    return DeveloperField(def: developerFieldDefinition)
}

final class DeveloperFieldTests: XCTestCase {
    
    func test_constructor_fromDeveloperFieldDefinition_createsExpectedField() throws {
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescMesg = FieldDescriptionMesg()
        try fieldDescMesg.setDeveloperDataIndex(0)
        try fieldDescMesg.setFieldDefinitionNumber(0)
        try fieldDescMesg.setFitBaseTypeId(.float32)
        try fieldDescMesg.setFieldName(index: 0, value: "fieldName")
        try fieldDescMesg.setUnits(index: 0, value: "units")
        try fieldDescMesg.setNativeMesgNum(.record)
        try fieldDescMesg.setNativeFieldNum(RecordMesg.heartRateFieldNum)
        
        let developerFieldDefinition = DeveloperFieldDefinition(fieldDescriptionMesg: fieldDescMesg, developerDataIdMesg: developerDataIdMesg, size: 0)
        
        XCTAssertEqual(developerFieldDefinition.developerDataIndex, developerDataIdMesg.getDeveloperDataIndex())
        XCTAssertEqual(developerFieldDefinition.fieldDefinitionNumber, fieldDescMesg.getFieldDefinitionNumber())
        
        XCTAssertEqual(developerFieldDefinition.developerDataIdMesg?.getDeveloperId(), developerDataIdMesg.getDeveloperId())
        XCTAssertEqual(developerFieldDefinition.fieldDescriptionMesg?.getFieldDefinitionNumber(), fieldDescMesg.getFieldDefinitionNumber())
        
        let devField = DeveloperField(def: developerFieldDefinition)
        
        
        XCTAssertEqual(devField.getNum(), developerFieldDefinition.fieldDefinitionNumber)
        XCTAssertEqual(devField.getBaseType(), BaseType(rawValue: (developerFieldDefinition.fieldDescriptionMesg?.getFitBaseTypeId()!.rawValue)!))
        XCTAssertEqual(devField.getName(), "fieldName")
        XCTAssertEqual(devField.getUnits(), "units")
        XCTAssertEqual(devField.nativeOverride, RecordMesg.heartRateFieldNum)
    }
    
    func test_setDeveloperFieldAndCopyingField_copiesAllDeveloperFields() throws {
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescMesg = FieldDescriptionMesg()
        try fieldDescMesg.setDeveloperDataIndex(0)
        try fieldDescMesg.setFieldDefinitionNumber(0)
        try fieldDescMesg.setFitBaseTypeId(FitBaseType.float32)
        try fieldDescMesg.setFieldName(index: 0, value: "doughnutsearned")
        try fieldDescMesg.setUnits(index: 0, value: "doughnuts")
        try fieldDescMesg.setNativeMesgNum(MesgNum.record)
        try fieldDescMesg.setNativeFieldNum(RecordMesg.heartRateFieldNum)
        
        let developerFieldDefinition = DeveloperFieldDefinition(fieldDescriptionMesg: fieldDescMesg, developerDataIdMesg: developerDataIdMesg, size: 0)
        
        let devField = DeveloperField(def: developerFieldDefinition)
        try devField.setValue(value: 25)

        let recordMesg = RecordMesg()
        recordMesg.setDeveloperField(devField)
        try recordMesg.setHeartRate(20)
        
        let field = recordMesg.getDeveloperField(developerDataIdMesg: developerFieldDefinition.developerDataIdMesg!, fieldDescriptionMesg: developerFieldDefinition.fieldDescriptionMesg!)
        
        XCTAssertEqual(field, devField)
        
        // Test that creating a new message from an existing message copies developer fields
        let recordMesg2 = RecordMesg(mesg: recordMesg)
        
        let field2 = recordMesg2.getDeveloperField(developerDataIdMesg: developerFieldDefinition.developerDataIdMesg!, fieldDescriptionMesg: developerFieldDefinition.fieldDescriptionMesg!)
        
        XCTAssertEqual(field2, field)
    }
    
    func test_equatable_whenValuesAreSameOrIdentical_returnsExpectedValue() throws {
        let devField = try createTestDeveloperField()
        
        let testCases = [
            (title: "Identical Dev Field", value: try createTestDeveloperField(), expected: true),
            (title: "Dev Field with Different Name", value: try createTestDeveloperField(fieldName: "Field1"), expected: false),
            (title: "Dev Field with Different Field num", value: try createTestDeveloperField(fieldDefinitionNumber: 1), expected: false),
            (title: "Dev Field with Different Type", value: try createTestDeveloperField(fitBaseType: FitBaseType.uint8), expected: false),
            (title: "Dev Field with Different Scale", value: try createTestDeveloperField(scale: 10), expected: false),
            (title: "Dev Field with Different Offset", value: try createTestDeveloperField(offset: 100), expected: false),
            (title: "Dev Field with Different Units", value: try createTestDeveloperField(units: "m"), expected: false),
        ] as [(String, DeveloperField, Bool)]
        
        for (title, value, expected) in testCases {
            XCTContext.runActivity(named: title) { activity in
                XCTAssertEqual((devField == value), expected)
            }
        }
    }
}

