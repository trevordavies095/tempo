/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import Foundation

import XCTest
@testable import FITSwiftSDK

final class DeveloperDataLookupTests: XCTestCase {

    func test_developerDataKeyEquatable_whenBothAreIdentical_returnsEqualTrue() throws {
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescriptionMesg = FieldDescriptionMesg()
        try fieldDescriptionMesg.setFieldDefinitionNumber(0)
        
        let devDataKey = DeveloperDataKey(developerDataIdMesg: developerDataIdMesg, fieldDescriptionMesg: fieldDescriptionMesg)
        
        let devDataKeyIdentical = DeveloperDataKey(developerDataIdMesg: developerDataIdMesg, fieldDescriptionMesg: fieldDescriptionMesg)
        
        XCTAssertEqual(devDataKey, devDataKeyIdentical)
        XCTAssertEqual(devDataKey?.hashValue, devDataKeyIdentical?.hashValue)
    }
    
    func test_developerDataKeyEquatable_whenBothAreDifferent_returnsEqualFalse() throws {
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescriptionMesg = FieldDescriptionMesg()
        try fieldDescriptionMesg.setFieldDefinitionNumber(0)
        
        let developerDataIdMesgOther = DeveloperDataIdMesg()
        try developerDataIdMesgOther.setDeveloperDataIndex(1)
        
        let fieldDescriptionMesgOther = FieldDescriptionMesg()
        try fieldDescriptionMesgOther.setFieldDefinitionNumber(1)
        
        let devDataKey = DeveloperDataKey(developerDataIdMesg: developerDataIdMesg, fieldDescriptionMesg: fieldDescriptionMesg)
        
        let devDataKeyDifferent = DeveloperDataKey(developerDataIdMesg: developerDataIdMesgOther, fieldDescriptionMesg: fieldDescriptionMesgOther)
        
        XCTAssertNotEqual(devDataKey, devDataKeyDifferent)
        XCTAssertNotEqual(devDataKey?.hashValue, devDataKeyDifferent?.hashValue)
    }
    
    func test_developerDataKeyEquatable_whenKeyValuePairsEqualButSwapped_returnsEqualFalse() throws {
        let developerDataIdMesg0 = DeveloperDataIdMesg()
        try developerDataIdMesg0.setDeveloperDataIndex(0)
        
        let fieldDescriptionMesg0 = FieldDescriptionMesg()
        try fieldDescriptionMesg0.setFieldDefinitionNumber(0)
        
        let developerDataIdMesg1 = DeveloperDataIdMesg()
        try developerDataIdMesg1.setDeveloperDataIndex(1)
        
        let fieldDescriptionMesg1 = FieldDescriptionMesg()
        try fieldDescriptionMesg1.setFieldDefinitionNumber(1)
        
        let devDataKey01 = DeveloperDataKey(developerDataIdMesg: developerDataIdMesg0, fieldDescriptionMesg: fieldDescriptionMesg1)
        
        let devDataKey10 = DeveloperDataKey(developerDataIdMesg: developerDataIdMesg1, fieldDescriptionMesg: fieldDescriptionMesg0)
        
        XCTAssertNotEqual(devDataKey01, devDataKey10)
        XCTAssertNotEqual(devDataKey01?.hashValue, devDataKey10?.hashValue)
    }
    
    func test_getDeveloperFieldDefinition_returnsEpxectedDeveloperFieldDefinition() throws {
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescMesg = FieldDescriptionMesg()
        try fieldDescMesg.setDeveloperDataIndex(0)
        try fieldDescMesg.setFieldDefinitionNumber(0)

        DeveloperDataLookup.shared.addDeveloperDataIdMesg(mesg: developerDataIdMesg)
        DeveloperDataLookup.shared.addFieldDescriptionMesg(mesg: fieldDescMesg)
        
        let retrieved = DeveloperDataLookup.shared.getDeveloperFieldDefinition(developerDataIdMesg: developerDataIdMesg, fieldDescriptionMesg: fieldDescMesg)
        XCTAssertEqual(retrieved?.developerDataIdMesg?.getDeveloperDataIndex(), developerDataIdMesg.getDeveloperDataIndex())
        XCTAssertEqual(retrieved?.fieldDescriptionMesg?.getFieldDefinitionNumber(), fieldDescMesg.getFieldDefinitionNumber())
    }
    
    func test_getDeveloperFieldDefintion_WhenEitherMesgWasUnadded_returnsNil() throws {
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescMesg = FieldDescriptionMesg()
        try fieldDescMesg.setDeveloperDataIndex(0)
        try fieldDescMesg.setFieldDefinitionNumber(0)
        
        let unaddedDeveloperDataIdMesg = DeveloperDataIdMesg()
        let unaddedFieldDescMesg = FieldDescriptionMesg()

        DeveloperDataLookup.shared.addDeveloperDataIdMesg(mesg: developerDataIdMesg)
        DeveloperDataLookup.shared.addFieldDescriptionMesg(mesg: fieldDescMesg)
        
        var retrieved = DeveloperDataLookup.shared.getDeveloperFieldDefinition(developerDataIdMesg: unaddedDeveloperDataIdMesg, fieldDescriptionMesg: fieldDescMesg)
        XCTAssertNil(retrieved)
        
        retrieved = DeveloperDataLookup.shared.getDeveloperFieldDefinition(developerDataIdMesg: developerDataIdMesg, fieldDescriptionMesg: unaddedFieldDescMesg)
        XCTAssertNil(retrieved)
    }
    
    func test_addingOverlappingMesgs() throws {
        let developerDataIdMesgOriginal = DeveloperDataIdMesg()
        try developerDataIdMesgOriginal.setDeveloperDataIndex(0)
        
        let fieldDescMesgOriginal = FieldDescriptionMesg()
        try fieldDescMesgOriginal.setDeveloperDataIndex(0)
        try fieldDescMesgOriginal.setFieldDefinitionNumber(0)
        try fieldDescMesgOriginal.setFieldName(index: 0, value: "original")
        
        let developerDataIdMesgNew = DeveloperDataIdMesg()
        try developerDataIdMesgNew.setDeveloperDataIndex(0)
        
        let fieldDescMesgNew = FieldDescriptionMesg()
        try fieldDescMesgNew.setDeveloperDataIndex(0)
        try fieldDescMesgNew.setFieldDefinitionNumber(0)
        try fieldDescMesgNew.setFieldName(index: 0, value: "new")

        DeveloperDataLookup.shared.addDeveloperDataIdMesg(mesg: developerDataIdMesgOriginal)
        DeveloperDataLookup.shared.addFieldDescriptionMesg(mesg: fieldDescMesgOriginal)
        
        var retrieved = DeveloperDataLookup.shared.getDeveloperFieldDefinition(developerDataIdMesg: developerDataIdMesgOriginal, fieldDescriptionMesg: fieldDescMesgOriginal)
        XCTAssertNotNil(retrieved?.developerDataIdMesg)
        XCTAssertNotNil(retrieved?.fieldDescriptionMesg)
        XCTAssertEqual(retrieved?.fieldDescriptionMesg?.getFieldName(), ["original"])
        
        // Add a new field description with field definition number of 0, the original should be overwritten
        DeveloperDataLookup.shared.addFieldDescriptionMesg(mesg: fieldDescMesgNew)
        retrieved = DeveloperDataLookup.shared.getDeveloperFieldDefinition(developerDataIdMesg: developerDataIdMesgOriginal, fieldDescriptionMesg: fieldDescMesgOriginal)
        XCTAssertEqual(retrieved?.fieldDescriptionMesg?.getFieldName(), ["new"])
        
        // Add a new DeveloperDataIdMesg with an existing developerDataIndex, it should erase all connected FieldDescriptionMesgs
        DeveloperDataLookup.shared.addDeveloperDataIdMesg(mesg: developerDataIdMesgNew)
        
        retrieved = DeveloperDataLookup.shared.getDeveloperFieldDefinition(developerDataIdMesg: developerDataIdMesgOriginal, fieldDescriptionMesg: fieldDescMesgOriginal)
        XCTAssertNil(retrieved)
    }
}
