# Push Notifications

Push Notifications allow you to reach users irrespective of whether your application is running in the foreground, the background or has been terminated, even when your application is not connected to Ably.

On iOS, Ably connects to [APNs](https://developer.apple.com/library/archive/documentation/NetworkingInternet/Conceptual/RemoteNotificationsPG/APNSOverview.html) to send messages to devices. On Android, Ably connects to [Firebase Cloud Messaging](https://firebase.google.com/docs/cloud-messaging/) to send messages to devices. As both services do not guarantee message delivery and may even throttle messages to specific devices based on battery level, message frequency, and other criteria, messages may arrive much later than sent or ignored.

## Known Limitations
 
- Requires initialising `AblyRealtime`: Currently activating push notifications for a device requires an instance of `AblyRealtime` which gets instantiated when the application starts up. Please, submit an issue if you do not require a realtime instance and would like to use Xamarin push notifications without using an `AblyRealtime` connection. For the time being when calling `(AndroidMobileDevice|AppleMobileDevice).Initialise` please set `ClientOptions.AutoConnect` to `false` which will keep the `AblyRealtime` instance from using any resources.

## Supported platforms

- Android API level 19+ (Android 4.4+)
    - Android devices
    - Android emulator (with Google APIs)
- iOS 10+
    - Physical devices only
    - **Not supported:** iOS Simulator. Calling [`UIApplication:registerForRemoteNotifications`](https://developer.apple.com/documentation/uikit/uiapplication/1623078-registerforremotenotifications) will result in [`application:didFailToRegisterForRemoteNotificationsWithError`](https://developer.apple.com/documentation/uikit/uiapplicationdelegate/1622962-application) method being called in your AppDelegate with an error: `remote notifications are not supported in the simulator`). This is an iOS simulator limitation.
    
## Setting up the DotNetPush App

To get push notifications setup in your own app, read [Setting up your own app](#setting-up-your-own-app).

### Android

The client library is available as a [nuget package](https://www.nuget.org/packages/ably.io.push.android/).

You can install it from the Package Manager Console using this command

```shell
PM> Install-Package ably.io.push.android
```

or using the .NET CLI in your project directory using

```shell
dotnet add package ably.io.push.android
```

- Open the DotnetPush solution which can be found in the `examples` folder in the repository, make sure to change the application Id in the Manifest file.
- Create a Firebase project, and in the Project settings, add an android app with your unique application ID. Follow the steps provided on the setup process, or the following:
    - You can leave `Debug signing certificate SHA-1` empty.
    - Download the generated `google-services.json` file
    - Place `google-services.json` in `example/android/app/`. We have `gitignore`d this file since it is associated with our Firebase project, but it is [not sensitive](https://stackoverflow.com/questions/37358340/should-i-add-the-google-services-json-from-firebase-to-my-repository), so you can commit it to share it with other developers/colleagues.
- Provide Ably with the FCM server key: In your [firebase project settings](https://knowledge.ably.com/where-can-i-find-my-google/firebase-cloud-messaging-api-key), create or use an existing cloud messaging server key, and enter it in your Ably app's dashboard (App > Notifications tab > Push Notifications Setup > Setup Push Notifications).
- You can follow the excellent [Android push notifications tutorial](https://ably.com/tutorials/android-push-notifications#setup-ably-account) which includes screenshots for the steps you need to take to link your Ably account with Firebase messaging.

### iOS

The client library is available as a [nuget package](https://www.nuget.org/packages/ably.io.push.ios/).

You can install it from the Package Manager Console using this command

```shell
PM> Install-Package ably.io.push.ios
```

or using the .NET CLI in your project directory using

```shell
dotnet add package ably.io.push.ios
```

- You need to have a [Apple developer program](https://developer.apple.com/programs/) membership ($99/year)
- Open your iOS app in Xcode: when in your project directory, run `xed ios` or double click `ios/Runner.xcworkspace` in `your_project_name/ios`
    - Add your developer account in Xcode, in `Preferences` > `Accounts`.
    - In the project navigator, click `Runner` > Click `Runner` target (not project) > `General`. Change the bundle identifier to a unique identifier. Then, under the `Signing & Capabilities` tab > `Team` dropdown menu, select your developer team associated with your developer account. This will register your bundle ID on App Store connect if it is not yet registered.
    - Create a push notification certificate (`.p12`) and upload it to the Ably dashboard to allow Ably to authenticate with APNs on your behalf, using [How do I obtain the APNs certificates needed for iOS Push Notifications?](https://knowledge.ably.com/how-do-i-obtain-the-apns-certificates-needed-for-ios-push-notifications).
    - Add `Push Notifications` capability: Click Runner in project navigator, click `Runner` target, under the **Signing & Capabilities** tab, click `+ Capability`, and select `Push Notifications`.
    - Add `remote notification` background mode:
        - Under the **Signing & Capabilities** tab, click `+ Capability` and select `Background Modes`.
        - Check `remote notifications`.
- The above is also described in [our native ios tutorial](https://ably.com/tutorials/ios-push-notifications#title).

## Usage

### Summary

Devices need to be [activated](#device-activation) with Ably once. Once activated, you can use their device ID, client ID or push token (APNs device token/ FCM registration token) to push messages to them using the Ably dashboard or a [Push Admin](https://ably.com/docs/general/push/admin) (SDKs which provide push admin functionality, such as the .net sdk, [Ably-java](https://github.com/ably/ably-java), [Ably-js](https://github.com/ably/ably-js), etc.). However, to send push notifications through Ably channels, devices need to [subscribe to a channel for push notifications](#subscribing-to-channels-for-push-notifications). Once subscribed, messages on that channel with a [push payload](#sending-messages) will be sent to devices which are subscribed to that channel.

The [DotNetPush app](https://github.com/ably/ably-pubsub-dotnet/tree/main/examples/DotnetPush) contains an example of how to use the Push Notification functionality. For Android devices please refer to [MainActivity.cs](https://github.com/ably/ably-pubsub-dotnet/blob/main/examples/DotnetPush/DotnetPush.Android/MainActivity.cs) and [MyFirebaseMessaging.cs](https://github.com/ably/ably-pubsub-dotnet/blob/main/examples/DotnetPush/DotnetPush.Android/MyFirebaseMessaging.cs). For iOS please refer to [AppDelegate](https://github.com/ably/ably-pubsub-dotnet/blob/main/examples/DotnetPush/DotnetPush.iOS/AppDelegate.cs).

### Device activation

- The first step is to initialise the specific mobile device. You can achieve this by using either `AppleMobileDevice.Initialise` or `AndroidModileDevice.Initialise`. To initialise you need to pass a valid Ably `ClientOptions` which will be used to initialise the Ably realtime client and an instance of `PushCallbacks` which you can use to recieve notification when the device is `Activated`, `Deactivated` or `SyncRegistrationFailed`.
- The initialise method will create an instance of `IRealtimeClient` which can be used to activate push notifications for the device by calling `client.Push.Activate()`. 
- Active is not guaranteed to happen straight away. The developer will be notified by calling `ActivatedCallback` which was passed to the `Initialise` method when hooking up push notifications

```csharp
public override bool FinishedLaunching(UIApplication app, NSDictionary options)
{
    Xamarin.Forms.Forms.Init();
    InitialiseAbly();
    //...

    return base.FinishedLaunching(app, options);
}

private void InitialiseAbly()
{
    var clientId = ""; // Set a clientId or generate one. It can be later used to send push notifications to the device.
    var callbacks = new PushCallbacks
    {
        ActivatedCallback = error => { /* handle notification */ },
        DeactivatedCallback = error => { /* handle notification */ },
        SyncRegistrationFailedCallback = error => { /* handle notification */ },
    };
    _realtime = AppleMobileDevice.Initialise(GetAblyOptions(savedClientId), callbacks);

    _realtime.Connect();
}

private ClientOptions GetAblyOptions(string savedClientId)
{
    var options = new ClientOptions
    {
        // https://ably.com/docs/best-practice-guide#auth
        // recommended for security reasons. Please, review Ably's best practise guide on Authentication
        // Please provide a way for AblyRealtime to connect to the services. Having API keys on mobile devices is not
        Key = "<key>",
        ClientId = string.IsNullOrWhiteSpace(savedClientId) ? Guid.NewGuid().ToString("D") : savedClientId,
    };

    _realtime = new AblyRealtime(options);
    // If you are going to use the AblyRealtime client in the app feel free to call Connect here
    // If not then skip it and add `AutoConnect = false,` to the ClientOptions.
    _realtime.Connect();

    return options;
}
```

### Subscribing to channels for push notifications

Use the `AblyRealtime` returend by `Initialise`.

- Get the Realtime/ Rest channel: `var channel = realtime.Channels.Get("channelName")`
- Subscribe the device to the **push channel**, by either using the device ID or client ID:
    - `channel.Push.SubscribeClient()` or `channel.Push.SubscribeDevice()`
    - This is different to subscribing to a channel for messages.
- Your device is now ready to receive and display user notifications (called alert notifications on iOS and notifications on Android) to the user, when the application is in the background.
- For debugging: You could use the Ably dashboard (notification tab) or the Push Admin API using another SDK to ensure the device has been subscribed by listing the subscriptions on a specific channel. Alternatively, you can list the push channels the device or client is subscribed to: `var subscriptions = channel.Push.ListSubscriptions()`. This API requires Push Admin capability, and should be used for debugging. This means you must use an Ably API key or token with the `push-admin` capability.

### Sending Messages

Once you have subscribed to recieve push notifications for a channel, other devices can send messages with specific `MessageExtras` which will trigger a push notification to be sent to your device.

#### Notification Message / Alert Push Notification

Shows a notification to the user immediately when it is received by their device.

**Android**: This is known as a [notification message](https://firebase.google.com/docs/cloud-messaging/concept-options). A notification message cannot be customised or handled (e.g. run logic when a user taps the notification) - therefore, if you need to handle user taps or customize a notification, send a data message and [create a local notification](https://developer.android.com/guide/topics/ui/notifiers/notifications).

**iOS**: This is known as an [alert push notification](https://developer.apple.com/documentation/usernotifications/). An alert notification can be [customised](https://developer.apple.com/documentation/usernotificationsui/customizing_the_appearance_of_notifications).

This is how you construct a message with a push notification. Normal subscribers will only receive the message where devices subscribed to that channel will receive the Push notification.

```csharp
var extrasText = @"{
    ""push"": {
      ""notification"": {
        ""title"": ""Hello from Ably."", 
        ""body"": ""Example push notification from Ably."",
        ""sound"": ""default"",
    },
  },
}
";

var extras = new MessageExtras(JToken.Parse(extrasText));
var message = new Message("messageName", "This is an Ably message published on channels that is also sent as a notification message to registered push devices.", messageExtras: extras); // Optional data field sent will not be received in push messages.
```

#### Data Message / Background Notification

Allows you to run logic in your application, such as download the latest content, perform local processing and creating a local notification.

**Android**: This is known as a [data message](https://firebase.google.com/docs/cloud-messaging/concept-options).

**iOS**: This is known as a background notification. These messages must have a priority of `5`, a push-type of `background`, and the `content-available` set to `1`, as shown in the code snippet below. To learn more about the message structure required by APNs, read [
Pushing Background Updates to Your App](https://developer.apple.com/documentation/usernotifications/setting_up_a_remote_notification_server/pushing_background_updates_to_your_app). You may see this documented as "silent notification" in Firebase documentation.

On iOS, a background notification may be throttled to 2 or 3 messages per hour, or limited for other reasons (for example, your app was just installed recently). To ensure your messages arrive promptly, you may send a message with both notification and data, which will show a notification to the user.

```csharp

var extras = new MessageExtras(JToken.Parse(@"{
    ""push"": {
      ""data"": {""foo"": ""bar"", ""baz"": ""quz""},
      ""apns"": {
        ""apns-headers"": {
          ""apns-push-type"": ""background"",
          ""apns-priority"": ""5"",
        },
        ""aps"": {
          ""content-available"": 1
        }
    }
  },
}
"));

var message = new Message("messageName", "This is an Ably message published on channels that is also sent as a notification message to registered push devices.", messageExtras: extras); // Optional data field sent will not be received in push messages.

```

You can find an example of how to receive Push notification messages in the DotNetPush sample app.
For iOS check the `DidReceiveRemoteNotification` method inside `AppDelegate.cs` and for Android check `OnMessageReceived` method inside `MyFirebaseMessaging.cs`

#### Prioritising messages

Only use high priority when it requires immediate user attention or interaction. Use the normal priority (5) otherwise. Messages with a high priority wake a device from a battery saving state, which drains the battery even more.
- High priority: `'priority': 'high'` inside `push.fcm.android` for Android. `apns-priority: '10'` inside `push.apns.apns-headers` for iOS.
- Normal priority: `'priority': 'normal'` inside `push.fcm.android` for Android. `apns-priority: '5'` inside `push.apns.apns-headers` for iOS.

#### Alert Notification **and** Background / Data Message

Push notifications containing both the notification and data objects will be treated as both alert notifications and data messages.

### Receiving Messages

For examples of handling incoming messages and dealing with notifications, see [push_notification_handlers](example/lib/push_notifications/push_notification_handlers.dart) in the example app.

#### Notification Message / Alert Push Notification

**Android**: If the app is in the background / terminated, you cannot configure / disable notification messages as they are automatically shown to the user by Firebase Messaging Android SDK. To create notifications which launch the application to a certain page (notifications which contain deep links or app links), or notifications which contain buttons / actions, images, and inline replies, you should send a data message and create a notification when the message is received. 

**iOS**: If the app is in the background / terminated, the Ably sdk doesn't provide the functionality to configure / extend alert notifications on iOS, and these will automatically be shown to the user.

### Deactivating the device

Do this only if you do not want the device to receive push notifications at all. 

```csharp
    realtime.Push.Deactivate();
```
