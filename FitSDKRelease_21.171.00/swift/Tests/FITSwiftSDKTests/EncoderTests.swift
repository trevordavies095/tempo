/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import XCTest
@testable import FITSwiftSDK

final class EncoderTests: XCTestCase {

    @available(iOS 16.0, macOS 13.0, *)
    func test_close_encoderHasReceivedNoMesgs_writesFileData() throws {
        try XCTSkipIf(true, "Where should the test files go?")

        let encoder = Encoder();
        let data = encoder.close()
        
        XCTAssertEqual(data.count,16)
        
        let fileURL = URL(string: "first.fit" , relativeTo: URL.downloadsDirectory)
        try data.write(to: fileURL!)
    }
    
    @available(iOS 16.0, macOS 13.0, *)
    func test_close_encoderHasReceivedOneMesg_writesFileData() throws {
        try XCTSkipIf(true, "Where should the test files go?")
        
        let encoder = Encoder();
        
        let fileIdMesg = FileIdMesg()
        try fileIdMesg.setType(.activity)
        try fileIdMesg.setTimeCreated(DateTime())
        try fileIdMesg.setProductName("Product Name")
        
        encoder.onMesg(fileIdMesg)
        let data = encoder.close()
        
        let fileURL = URL(string: "third.fit" , relativeTo: URL.downloadsDirectory)
        try data.write(to: fileURL!)
    }
    
    @available(iOS 16.0, macOS 13.0, *)
    func test_close_encoderHasReceivedMultipleMesgs_writesFileData() throws {
        try XCTSkipIf(true, "Where should the test files go?")
        
        let encoder = Encoder();
        
        let fileIdMesg = FileIdMesg()
        
        try fileIdMesg.setType(.activity)
        
        encoder.onMesg(fileIdMesg) // Should write a mesg definition
        encoder.onMesg(fileIdMesg) // Should not write a mesg definition
        
        try fileIdMesg.setTimeCreated(DateTime())
        encoder.onMesg(fileIdMesg) // Should write a mesg definition
        encoder.onMesg(fileIdMesg) // Should not write a mesg definition
        
        let data = encoder.close()
        
        let fileURL = URL(string: "TwoMesgDefinitions.fit" , relativeTo: URL.downloadsDirectory)
        try data.write(to: fileURL!)
    }
    
    @available(iOS 16.0, macOS 13.0, *)
    func test_close_encoderHasReceivedMesgsWithDeveloperData_writesFileData() throws {
        let encoder = Encoder();
        
        let fileIdMesg = FileIdMesg()
        try fileIdMesg.setType(.activity)
        encoder.onMesg(fileIdMesg)
        
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        encoder.onMesg(developerDataIdMesg)
        
        let fieldDescriptionMesg = FieldDescriptionMesg()
        try fieldDescriptionMesg.setDeveloperDataIndex(0)
        try fieldDescriptionMesg.setFieldDefinitionNumber(0)
        try fieldDescriptionMesg.setFitBaseTypeId(.uint8)
        try fieldDescriptionMesg.setFieldName(index: 0, value: "DeveloperField")
        encoder.onMesg(fieldDescriptionMesg)
        
        let recordMesg = RecordMesg()
        try recordMesg.setHeartRate(60)
        // Create the developer field
        let developerField = DeveloperField(fieldDescription: fieldDescriptionMesg, developerDataIdMesg: developerDataIdMesg)
        try developerField.setValue(value: 63 as Int)
        recordMesg.setDeveloperField(developerField)
        encoder.onMesg(recordMesg)
        
        
        let data = encoder.close()
        let fileURL = URL(string: "fitFileShortDevData.fit" , relativeTo: URL.downloadsDirectory)
        try data.write(to: fileURL!)
    }
}
