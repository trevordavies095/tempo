/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import XCTest
@testable import FITSwiftSDK

let packageRootPath = URL(fileURLWithPath: #file).pathComponents
    .prefix(while: { $0 != "Tests" }).joined(separator: "/").dropFirst()
let testDataPath = packageRootPath + "/Tests/FITSwiftSDKTests/TestData"

final class EncodeDecodeIntegrationTests: XCTestCase {
    
    func test_whenEncodingAndThenDecodingFileWithDeveloperData_noErrorsThrownAndDecoderResultsExpected() throws {
        let encoder = Encoder();
        
        let fileIdMesg = FileIdMesg()
        try fileIdMesg.setType(.activity)
        
        let developerDataIdMesg = DeveloperDataIdMesg()
        try developerDataIdMesg.setDeveloperDataIndex(0)
        
        let fieldDescriptionMesg = FieldDescriptionMesg()
        try fieldDescriptionMesg.setDeveloperDataIndex(0)
        try fieldDescriptionMesg.setFieldDefinitionNumber(0)
        try fieldDescriptionMesg.setFitBaseTypeId(.uint8)
        try fieldDescriptionMesg.setFieldName(index: 0, value: "DeveloperField")
        
        let recordMesg = RecordMesg()
        try recordMesg.setHeartRate(60)
        // Create the developer field
        let developerField = DeveloperField(fieldDescription: fieldDescriptionMesg, developerDataIdMesg: developerDataIdMesg)
        try developerField.setValue(value: 63 as Int)
        recordMesg.setDeveloperField(developerField)
        
        encoder.write(mesgs: [fileIdMesg, developerDataIdMesg, fieldDescriptionMesg, recordMesg])
        
        let encodedData = encoder.close()
        
        let fileURL = URL(string: "testDeveloperData.fit" , relativeTo: URL(fileURLWithPath: String(testDataPath)))
        try encodedData.write(to: fileURL!)
        
        
        let stream = FITSwiftSDK.InputStream(data: encodedData)
        
        let decoder = Decoder(stream: stream)
        
        let mesgListener = FitListener()
        decoder.addMesgListener(mesgListener)
        
        try decoder.read();
        
        let decodedFileIdMesg = mesgListener.fitMessages.fileIdMesgs[0]
        XCTAssertEqual(fileIdMesg.getType(), decodedFileIdMesg.getType())
        
        let decodedDeveloperDataIdMesg = mesgListener.fitMessages.developerDataIdMesgs[0]
        XCTAssertEqual(developerDataIdMesg.getDeveloperDataIndex(), decodedDeveloperDataIdMesg.getDeveloperDataIndex())
        
        let decodedFieldDescriptionMesg = mesgListener.fitMessages.fieldDescriptionMesgs[0]
        XCTAssertEqual(fieldDescriptionMesg.getDeveloperDataIndex(), decodedFieldDescriptionMesg.getDeveloperDataIndex())
        XCTAssertEqual(fieldDescriptionMesg.getFieldDefinitionNumber(), decodedFieldDescriptionMesg.getFieldDefinitionNumber())
        XCTAssertEqual(fieldDescriptionMesg.getFitBaseTypeId(), decodedFieldDescriptionMesg.getFitBaseTypeId())
        XCTAssertEqual(fieldDescriptionMesg.getFieldName(), decodedFieldDescriptionMesg.getFieldName())
        
        let decodedRecordMesg = mesgListener.fitMessages.recordMesgs[0]
        XCTAssertEqual(recordMesg.getHeartRate(), decodedRecordMesg.getHeartRate())
        
        let decodedDeveloperFieldDefinition = DeveloperFieldDefinition(fieldDescriptionMesg: decodedFieldDescriptionMesg, developerDataIdMesg: decodedDeveloperDataIdMesg, size: 0)
        let decodedDeveloperField = recordMesg.getDeveloperField(developerDataIdMesg: decodedDeveloperFieldDefinition.developerDataIdMesg!, fieldDescriptionMesg: decodedDeveloperFieldDefinition.fieldDescriptionMesg!)
        XCTAssertEqual(developerField.getValue() as! UInt8, decodedDeveloperField?.getValue() as! UInt8 )
    }
}
