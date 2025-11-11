/////////////////////////////////////////////////////////////////////////////////////////////
// Copyright 2025 Garmin International, Inc.
// Licensed under the Flexible and Interoperable Data Transfer (FIT) Protocol License; you
// may not use this file except in compliance with the Flexible and Interoperable Data
// Transfer (FIT) Protocol License.
/////////////////////////////////////////////////////////////////////////////////////////////


import XCTest
@testable import FITSwiftSDK

final class BitStreamTests: XCTestCase {
    
    func test_readBit_whenBitStreamFromByteArray_returnsExpectedValues() throws {
        let values: [UInt8] = [0xAA, 0xFF]
        let bitStream = try BitStream(values: values)
        let expectedValues: [UInt8] = [0, 1, 0, 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1]
        
        for (index, expectedvalue) in expectedValues.enumerated() {
            XCTAssertTrue(bitStream.hasBitsAvailable())
            XCTAssertTrue(bitStream.bitsAvailable == expectedValues.count - index)
            
            let value = try bitStream.readBit()
            XCTAssertEqual(expectedvalue, value)
        }
    }
    
    func test_readBit_whenBitStreamFromInteger_returnsExpectedValues() throws {
        let value: UInt16 = 0xAAFF
        let bitStream = try BitStream(value: value)
        let expectedValues: [UInt8] = [1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 0, 1, 0, 1]
        
        for (index, expectedvalue) in expectedValues.enumerated() {
            XCTAssertTrue(bitStream.hasBitsAvailable())
            XCTAssertTrue(bitStream.bitsAvailable == expectedValues.count - index)
            
            let value = try bitStream.readBit()
            XCTAssertEqual(expectedvalue, value)
        }
    }

    func test_readBits_fromArrayOfAnyIntegers_returnsExpectedValues() throws {
        let testCases = [
            (title: "UInt8 [0xAB] - 8", values: [UInt8](arrayLiteral: 0xAB), nBitsToRead: [8], expected: [0xAB]),
            (title: "UInt8 [0xAB] - 4,4", values: [UInt8](arrayLiteral: 0xAB), nBitsToRead: [4, 4], expected: [0xB, 0xA]),
            (title: "UInt8 [0xAB] - 4,1,1,1,1", datvaluesa: [UInt8](arrayLiteral: 0xAB), nBitsToRead: [4, 1, 1, 1, 1], expected: [0xB, 0x0, 0x1, 0x0, 0x1]),
            (title: "UInt8 [0xAA, 0xCB] - 16", values: [UInt8](arrayLiteral: 0xAA, 0xCB), nBitsToRead: [16], expected: [0xCBAA]),
            (title: "UInt8 [0xAA, 0xCB, 0xDE, 0xFF] - 16,16", values: [UInt8](arrayLiteral: 0xAA, 0xCB, 0xDE, 0xFF), nBitsToRead: [16, 16], expected: [0xCBAA, 0xFFDE]),
            (title: "UInt8 [0xAA, 0xCB, 0xDE, 0xFF] - 32", values: [UInt8](arrayLiteral: 0xAA, 0xCB, 0xDE, 0xFF), nBitsToRead: [32], expected: [0xFFDECBAA]),
            (title: "UInt16 [0xABCD, 0xEF01] - 32", values: [UInt16](arrayLiteral: 0xABCD, 0xEF01), nBitsToRead: [32], expected: [0xEF01ABCD]),
            (title: "UInt32 [0xABCDEF01] - 32", values: [UInt32](arrayLiteral: 0xABCDEF01), nBitsToRead: [32], expected: [0xABCDEF01]),
            (title: "UInt64 [0x7BCDEF0123456789] - 64", values: [UInt64](arrayLiteral: 0x7BCDEF0123456789), nBitsToRead: [64], expected: [Int64(0x7BCDEF0123456789)]),
            (title: "UInt64 [0xABCDEF0123456789] - 32", values: [UInt64](arrayLiteral: 0xABCDEF0123456789), nBitsToRead: [32], expected: [0x23456789]),
            (title: "UInt64 [0xABCDEF0123456789] - 32,32", values: [UInt64](arrayLiteral: 0xABCDEF0123456789), nBitsToRead: [32,32], expected: [0x23456789, 0xABCDEF01]),
        ] as [(String, [any Numeric], [Int], [Int64])]
                
        for (title, values, nBitsToRead, expected) in testCases {
            try XCTContext.runActivity(named: title) { activity in
                let bitStream = try BitStream(values: values)
                try assertBitStreamReadBitsIsExpected(bitStream: bitStream, nBitsToRead: nBitsToRead, expected: expected)
            }
        }
    }
    
    func test_readBits_fromAnyIntegers_returnsExpectedValues() throws {
        let testCases = [
            (title: "UInt8 0xAB - 8", value: UInt8(0xAB), nBitsToRead: [8], expected: [0xAB]),
            (title: "UInt8 0xAB - 4,4", value: UInt8(0xAB), nBitsToRead: [4, 4], expected: [0xB, 0xA]),
            (title: "UInt8 0xAB - 4,1,1,1,1", value: UInt8(0xAB), nBitsToRead: [4, 1, 1, 1, 1], expected: [0xB, 0x0, 0x1, 0x0, 0x1]),
            (title: "UInt8 0xAACB - 16", value: UInt16(0xAACB), nBitsToRead: [16], expected: [0xAACB]),
            (title: "UInt32 0xABCDEF01 - 16,16", value: UInt32(0xABCDEF01), nBitsToRead: [16, 16], expected: [0xEF01, 0xABCD]),
            (title: "UInt32 0xABCDEF01 - 32", value: UInt32(0xABCDEF01), nBitsToRead: [32], expected: [0xABCDEF01]),
            (title: "UInt64 0xABCDEF0123456789 - 64", value: UInt64(0x7BCDEF0123456789), nBitsToRead: [64], expected: [0x7BCDEF0123456789]),
            (title: "UInt64 0xABCDEF0123456789 - 32", value: UInt64(0xABCDEF0123456789), nBitsToRead: [32], expected: [0x23456789]),
            (title: "UInt64 [0xABCDEF0123456789] - 32,32", value: UInt64(0xABCDEF0123456789), nBitsToRead: [32, 32], expected: [0x23456789, 0xABCDEF01]),
        ] as [(String, any Numeric, [Int], [Int64])]
        
        for (title, value, nBitsToRead, expected) in testCases {
            try XCTContext.runActivity(named: title) { activity in
                let bitStream = try BitStream(value: value)
                try assertBitStreamReadBitsIsExpected(bitStream: bitStream, nBitsToRead: nBitsToRead, expected: expected)
            }
        }
    }
    
    func assertBitStreamReadBitsIsExpected(bitStream: BitStream, nBitsToRead: [Int], expected: [Int64]) throws {
        for (index, expectedValue) in expected.enumerated() {
            let actualValue = try bitStream.readBits(nBitsToRead[index])
            XCTAssertEqual(expectedValue, actualValue)
        }
    }
    
    // MARK: ReadBit and ReadBits Error Tests
    func test_readBits_whenNoBitsAvailable_throwsError() throws {
        let value: UInt32 = 0xABCDEFFF
        
        let bitStream = try BitStream(value: value)
        _ = try bitStream.readBits(32)
        
        XCTAssertThrowsError(try bitStream.readBits(2))
    }
    
    func test_readBit_whenNoBitsAvailable_throwsError() throws {
        let value: UInt8 = 0xAB
        
        let bitStream = try BitStream(value: value)
        _ = try bitStream.readBits(8)
        
        XCTAssertThrowsError(try bitStream.readBit())
    }
    
    func test_readBits_whenLengthToReadExceeds64Bits_throwsError() throws {
        let values: [UInt64] = [UInt64.max, UInt64.max]
        
        let bitStream = try BitStream(values: values)
    
        XCTAssertThrowsError(try bitStream.readBits(65))
    }
}
