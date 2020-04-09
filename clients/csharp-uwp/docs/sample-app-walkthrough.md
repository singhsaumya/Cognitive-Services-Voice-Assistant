# Understanding the sample app
This is a detailed breakdown of the steps to build a Windows voice assistant along with an explanation of how the UWP Voice Assistant sample implements each step. Please read the [readme]() for this sample for more information on MVA, DLS, and the UWP Voice Assistant Sample.

## Listen for voice activation
---
MVA uses a lower-power, always-on keyword detector to detect when a user speaks a registered keyword. This is referred to as "1st stage detection". These are the steps to for a voice assistant application to register a keyword with MVA and listen for 1st stage keyword detection:
1. [Ensure that the microphone is available and accessible, then monitor its state](Ensure-that-the-microphone-is-available-and-accessible,-then-monitor-its-state)
2. [Register the application with Background Service so that it can be activated as a background task](Register-the-application-with-Background-Service-so-that-it-can-be-activated-as-a-background-task)
3. [Register the keyword for the application](Register-the-keyword-for-the-application)
4. [Verify that the voice activation setting is enabled](Verify-that-the-voice-activation-setting-is-enabled)
5. [Retrieve a ConversationalAgentSession to register the app with the MVA system](Retrieve-a-ConversationalAgentSession-to-register-the-app-with-the-MVA-system)
6. [Listen to the two activation signals: the OnBackgroundActivated and OnSignalDetected](Listen-to-the-two-activation-signals:-the-OnBackgroundActivated-and-OnSignalDetected)

### 1. Ensure that the microphone is available and accessible, then monitor its state
MVA needs a microphone to be present and accessible to be able to detect a voice activation. AudioCaptureControl retrieves and monitors the state of audio input device, input volume, and whether the input is reported as muted. It also contains a reference to the MicrophoneCapability, which reflects whether the user has accepted microphone privacy settings. Call RequestAccessAsync on the MicrophoneCapability to launch a prompt for microphone access. Note that you'll need to include "microphone" as a capability in the app manifest file if you're using AudioCaptureControl in a different project.

### 2. Register the application with the background service
In order for MVA to launch the application in the background, the application needs to be registered with the Background Service. This is implemented in MVARegistrationHelper's IsBackgroundTaskRegistered property. Register your application by setting IsBackgroundTaskRegistered to true. This should be set on application start.

### 3. Register the keyword for the application
The application needs to register itself, its keyword model, and its language with MVA. MVA uses this information to listen for the keyword using the provided model and launch the correct application on detection. This is implemented in KeywordRegistration. Register your application by creating an instance of KeywordRegistration with the proper inputs. You can update these values with the UpdateKeyword method. This should be executed on application start.

### 4. Verify that the voice activation setting is enabled
To use voice activation, a user needs to enable voice activation for their system and enable voice activation for their application. You can find the setting under "Voice activation privacy settings" in Windows settings. To check the status of the voice activation setting in your application, you must have an instance of the ActivationSignalDetectionConfiguration from the Windows SDK. In the sample app, retrieve the current ActivationSignalDetectionConfiguration by calling GetOrCreateKeywordConfigurationAsync on the instance of KeywordRegistration from step 3. The AvailabilityInfo field on the ActivationSignalDetectionConfiguration contains an enum value that describes the state of the voice activation setting.

### 5. Retrieve a ConversationalAgentSession to register the app with the MVA system
The ConversationalAgentSession is a class in the Windows SDK that allows your app to update the MVA system with the app state (Idle, Detecting, Listening, Speaking) and receive events, such as activation detection and system state changes such as the screen locking. Retrieving an instance of the AgentSession also serves to register the application with MVA as activatable by voice. It is best practice to maintain one reference to the ConversationalAgentSession. In the sample app, this instance is managed by the AgentSessionManager and can be retrieved using GetSessionAsync. This should be called on application start.

### 6. Listen to the two activation signals: the OnBackgroundActivated and OnSignalDetected
MVA will signal your app when it detects a keyword in one of two ways. If the app is not active (ie you do not have a reference to a non-disposed instance of ConversationalAgentSession), then it will launch your app and call the OnBackgroundActivated method in the App.xaml.cs file of your application. If the BackgroundActivatedEventArgs' TaskInstance<span>.Task.Name matches "AgentBackgroundTrigger", it was triggered by an MVA activation. The application needs to override this method and retrieve an instance of ConversationalAgentSession. If the app is active (ie has a reference to a non-disposed instance of ConversationalAgentSession), then MVA will signal the app through the SignalDetected event in the ConversationalAgentSession. The app needs to subscribe to this event. In the sample app, the first case is handled in App.xaml.cs and the second is handled in AgentSessionWrapper. In both cases, the reaction eventually leads to a call to DialogManager.HandleSignalDetected, which executes the code to execute keyword verification.

## Keyword Verification
Once a voice agent application receives a signal from MVA, the next step is to verify that the keyword detection was valid. The model used in 1st stage keyword detection is low-power, but as a result, it has a high false accept rate. For the best experience, the audio from the 1st stage detection needs to be verified. In the sample application, this is done in two stages using DLS: 2nd stage detection, which uses a higher-power, more accurate keyword detector, followed by 3rd stage detection, which sends the signal to the cloud to be verified by an even more accurate and resource-intensive model. Fortunately, most of this complexity is encompassed by the Speech SDK.

The following steps describe how to complete keyword verification, using the sample application as an example of how to achieve this with Direct Line Speech through the Speech SDK.

### 7. Retrieve activation audio
Create an [AudioGraph](https://docs.microsoft.com/en-us/uwp/api/windows.media.audio.audiograph) and pass it to the CreateAudioDeviceInputNodeAsync of the ConversationalAgentSession. This will load the graph's audio buffer with the audio *starting approximately 3 seconds before MVA detected a keyword*. In the sample app, AgentAudioInputProvider's InitializeFromAgentSessionAsync method retrieves the audio from the ConversationalAgentSession. AgentAudioInputProvider fires an event, DataAvailable, when the audio is ready as a stream of bytes. 

Note: In the sample app, the first 2 seconds of the audio are trimmed before it is sent to Speech Services. This is because the keyword verification may fail if the full 3 seconds of audio before keyword detection are included in the audio.

### 8. Verify the keyword
Using Cognitive Speech Services or your own system, process the activation audio to verify whether it contains a keyword. The sample app uses the Speech SDK to interface with Speech Services. To use a different keyword verification system, direct the audio from the AgentAudioInputProvider's DataAvailable event to the alternative service and trigger step 9 on keyword confirmation. 

The following goes into further detail on how the sample application uses Direct Line Speech for keyword verification.
#### Initiate conversation start on signal from MVA
The DialogManager class manages the lifecycle of a bot interaction, accepting commands to start keyword verification and speech recognition and emitting events with recognition results and bot responses. In step 6, after receiving a signal from MVA, the sample app calls DialogManager.HandleSignalDetection, which initiates keyword verification. 
#### Pass input audio to Direct Line Speech
DialogManager completes step 7 by initializing the AgentAudioInputProvider and passing it to an instance of DirectLineSpeechDialogBackend through DirectLineSpeechDialogBackend.SetAudioSource. The DirectLineSpeechDialogBackend class manages the Speech SDK classes used to interface with Direct Line Speech. 
#### Initiate keyword verification
When the AgentAudioInputProvider fires the DataAvailable event, the bytes from the activation audio are passed to the Speech SDK and analyzed by the local 2nd stage keyword detector.
#### Handle 2nd stage keyword verification
When the 2nd stage keyword detector detects a keyword, the DialogServiceConnector object in DirectLineSpeechDialogBackend will fire the SpeechRecognizing event with the result reason ResultReason.RecognizingKeyword. Note: there is no "keyword rejected" signal from the DialogServiceConnector for 2nd stage verification, so there must be a manually implemented timeout to cancel verification in case of failure. In the sample application, this "rejection timer" is implemented in SignalDetectionHelper.
#### Handle 3rd stage keyword verification
On successful 2nd stage keyword verification, the DialogServiceConnector automatically sends the audio to the cloud for 3rd stage keyword verification. When it verifies the keyword with cloud verification, the DialogServiceConnector object will fire the SpeechRecognized event with the result reason ResultReason.RecognizedKeyword. If cloud verification fails, the result reason will be ResultReason.NoMatch. In either case, the DialogManager surfaces these events as SignalConfirmed and SignalRejected.

### 3. If the keyword is verified, continue in the background or move to the foreground to display UI
The app is running in the background after it is activated by MVA, so it cannot display UI. For an audio-only app interaction, your application can simply stay in the background, play audio output, and listen to audio input. To display UI, call ConversationalAgentSession.RequestForegroundActivationAsync. In the sample app, this is called in App.xaml.cs's OnSignalConfirmed which is triggered by the SignalConfirmed event in DialogManager.

### Summary of activation in the sample app
When the app is started, the sample app 
- verifies that an audio input device is available and accessible
- registers itself with the Background Service
- registers its keyword
- verifies the voice activation is enabled

When the sample app receives an activation from MVA: 
- DialogManager.HandleSignalDetected is called, either through App.xaml.cs's OnBackgroundActivated or the ConversationalAgentSession.SignalDetected event handler in AgentSessionManager
- HandleSignalDetected initializes the AgentAudioInputProvider with audio from the ConversationalAgentSession, creates a DirectLineSpeechDialogBackend instance, and passes the AgentAudioInputProvider through SetAudioSource
- DialogManager listens for speech recognition events from the DirectLineSpeechDialogBackend or a timeout event from SignalDetectionHelper and surfaces the events to the UI
- On keyword confirmation, the app uses the ConversationalAgentSession to request foreground activation and display UI

## Bot interaction
---
To interact with your bot, the Speech Services SDK provides a straightforward implementation that accepts an audio stream and outputs events with information on speech recognition and bot responses