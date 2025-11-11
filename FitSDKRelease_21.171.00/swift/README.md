# Garmin - FIT Swift SDK
## FIT SDK Documentation
The FIT SDK documentation is available at [https://developer.garmin.com/fit](https://developer.garmin.com/fit).

## FIT SDK Developer Forum
Share your knowledge, ask questions, and get the latest FIT SDK news in the [FIT SDK Developer Forum](https://forums.garmin.com/developer/).

## FIT Swift SDK Requirements
The FIT Swift SDK requires **macOS 12** or **iOS 14** and uses **Swift Tools Version 5.9**

## Install
In an Xcode project, select **File > Add Package Dependency** and enter the source control repository GitHub URL: https://github.com/garmin/fit-swift-sdk.git

## Usage
After the FIT Swift SDK package has been added as dependency, it can be used by importing the FITSwiftSDK module into the appropriate source files.
```swift 
import FITSwiftSDK
```
The package includes example codes, in the test folder, that demonstrate how to use the SDK. These test programs are similar to the [Cookbook](https://developer.garmin.com/fit/cookbook/) recipes.

* **[ActivityEncodeTests.swift](Tests/FITSwiftSDKTests/Examples/ActivityEncodeTests.swift)**: Demonstrates how to encode Activity Files.
* **[DecoderMesgBroadcasterTests.swift](Tests/FITSwiftSDKTests/Examples/DecoderMesgBroadcasterTests.swift)**: Demonstrates decoding a FIT file using the MesgBroadcaster and MesgListener protocol methods.
* **[DecoderFitListenerTests.swift](Tests/FITSwiftSDKTests/Examples/DecoderFitListenerTests.swift)**: Demonstrates decoding a FIT file using the FitListener and FitMessages classes.

To run the example tests, open **/path/to/fit/sdk/swift/package.swift** in Xcode, then go to the test navigator and press **⌘U**