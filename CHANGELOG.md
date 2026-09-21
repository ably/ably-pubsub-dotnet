# Change Log

## [2.0.0](https://github.com/ably/ably-pubsub-dotnet/tree/2.0.0)

Ably Pub/Sub 2.0 splits the SDK into device-side and server-side packages so that every client is classified for MAU-based billing. See [UPDATING.md](UPDATING.md) for the full migration guide.

**Breaking changes**

- The `ably.io` package is no longer published from this repository. It is replaced by three packages: **`Ably.PubSub.Device`** (end-user device apps — mobile, desktop, Unity, set-top boxes), **`Ably.PubSub.Server`** (backends — ASP.NET, workers, console apps) and **`Ably.PubSub.Core`** (the shared implementation, resolved transitively; do not reference it directly).
- Construct clients through the door factories, not the `AblyRealtime`/`AblyRest` constructors: `PubSubDevice.CreateClient(...)`, `PubSubServer.CreateRealtimeClient(...)`, `PubSubServer.CreateHttpClient(...)`. A client built directly from `Ably.PubSub.Core` is not classified as device- or server-side, and is rejected once MAU-based pricing is live.
- The runtime API is otherwise unchanged: the namespace stays `IO.Ably` and the factories return the ordinary `AblyRealtime`/`AblyRest`.

**Notes**

- Device push-receive (push activation on Android/iOS) is not part of the 2.0 packages; a `net8.0-android`/`net8.0-ios` port on `Ably.PubSub.Device` is a possible future follow-up (currently **parked** — no committed milestone). Push administration (from a backend) is unaffected. See [PushNotifications.md](PushNotifications.md).
- The `ably.io` 1.x line continues to receive security and critical fixes from its maintenance branch for one year from this release, then reaches end of life.

## [1.2.19](https://github.com/ably/ably-pubsub-dotnet/tree/1.2.19) (2026-09-11)

[Full Changelog](https://github.com/ably/ably-pubsub-dotnet/compare/1.2.18...1.2.19)

Implements RTN23 dead-transport detection and the connection-recovery spec points around it (detection latency drops from 120s to ~25s). No public API removed or changed, but the following are visible to callers:

**Behaviour changes:**

- `authCallback` is now bounded by `realtimeRequestTimeout` (default 10s) — previously unbounded. A callback that took longer and still succeeded will now fail the auth attempt. `authUrl` was already bounded, by the separate `httpRequestTimeout`, and is unaffected by this change.
- `HeartbeatMonitorDelay` rejects values below 1ms — previously accepted, but led to unintentional side-effects.
- `Connection.Key` and `Id` read as null during a `CLOSED`/`FAILED` state-change callback (cleared before the event is emitted), and are now retained through `SUSPENDED`.
- `connectionId` no longer changes after a long disconnection, since the client always attempts a resume now. Use `ChannelStateChange.Resumed` to detect a fresh connection.
- `Connection.CreateRecoveryKey()` returns a key while `SUSPENDED`, where it previously returned empty.
- Some `ErrorInfo` messages and codes changed.

**Merged pull requests:**

- Implement RTN23 idle-transport detection and the recovery spec points around it by @AndyTWF in https://github.com/ably/ably-pubsub-dotnet/pull/1331

## [1.2.18](https://github.com/ably/ably-dotnet/tree/1.2.18) (2025-11-27)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.17...1.2.18)

**Merged pull requests:**

- [ECO-5646] Fix behaviour when internet connection is not available in https://github.com/ably/ably-dotnet/pull/1320
- [ECO-5650] ClientOptions.HttpClient for better connection pool management in https://github.com/ably/ably-dotnet/pull/1323

## [1.2.17](https://github.com/ably/ably-dotnet/tree/1.2.17) (2025-11-10)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.16...1.2.17)

**Merged pull requests:**

- [ECO-5584] Upgrade NewtonSoft from 9.0.1 to 13.0.1 in https://github.com/ably/ably-dotnet/pull/1309
- [ECO-5624] Fix unity newtonsoft dependency in https://github.com/ably/ably-dotnet/pull/1313

## [1.2.16](https://github.com/ably/ably-dotnet/tree/1.2.16) (2025-10-15)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.15...1.2.16)

**Merged pull requests:**

- [ECO-4285] Fix build system by @sacOO7 in https://github.com/ably/ably-dotnet/pull/1300
- [ECO-4285] Fix delta compression by @sacOO7 in https://github.com/ably/ably-dotnet/pull/1304

## [1.2.15](https://github.com/ably/ably-dotnet/tree/1.2.15) (2023-04-19)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.14...1.2.15)

**Merged pull requests:**

- Refactored documentation and SDK supported capabilities by @sacOO7 in https://github.com/ably/ably-dotnet/pull/1274
- Fix Create token request by @sacOO7 in https://github.com/ably/ably-dotnet/pull/1282
- Fix entering internal presence members by @sacOO7 in https://github.com/ably/ably-dotnet/pull/1275
- Fix broken REST host fallbacks by @sacOO7 in https://github.com/ably/ably-dotnet/pull/1278

## [1.2.14](https://github.com/ably/ably-dotnet/tree/1.2.14) (2023-10-21)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.13...1.2.14)

**Fixed bugs:**

- Realtime Channel Status method [\#1240](https://github.com/ably/ably-dotnet/issues/1240)

**Closed issues:**

- No `async` version of `IRestChannel.Status` [\#1263](https://github.com/ably/ably-dotnet/issues/1263)
- Ably and ClientWebsocket [\#1257](https://github.com/ably/ably-dotnet/issues/1257)
- JSON Encoding issues [\#1255](https://github.com/ably/ably-dotnet/issues/1255)

**Merged pull requests:**

- Add async version of channel status [\#1266](https://github.com/ably/ably-dotnet/pull/1266) ([sacOO7](https://github.com/sacOO7))
- Fix ably request method param [\#1260](https://github.com/ably/ably-dotnet/pull/1260) ([sacOO7](https://github.com/sacOO7))

## [1.2.13](https://github.com/ably/ably-dotnet/tree/1.2.13) (2023-09-25)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.12...1.2.13)

**Implemented enhancements:**

- Upgrade library to protocol v2 [\#1199](https://github.com/ably/ably-dotnet/pull/1199)

**Closed issues:**

- Error getting value from "Id" on IO.Ably.Message, code 50000 \(ios-net7 deployed, maui\) [\#1259](https://github.com/ably/ably-dotnet/issues/1259)
- Channel messages are missed or delayed [\#1252](https://github.com/ably/ably-dotnet/issues/1252)
- Update connectionStateTtl [\#1243](https://github.com/ably/ably-dotnet/issues/1243)
- Update README for MAUI trimming config [\#1242](https://github.com/ably/ably-dotnet/issues/1242)
- Improve CI testing process [\#362](https://github.com/ably/ably-dotnet/issues/362)

**Merged pull requests:**

- Fix readme [\#1256](https://github.com/ably/ably-dotnet/pull/1256) ([sacOO7](https://github.com/sacOO7))
- Updated connectionTtl to be 120 seconds [\#1247](https://github.com/ably/ably-dotnet/pull/1247) ([sacOO7](https://github.com/sacOO7))
- MAUI config doc [\#1245](https://github.com/ably/ably-dotnet/pull/1245) ([sacOO7](https://github.com/sacOO7))

## [1.2.12](https://github.com/ably/ably-dotnet/tree/1.2.12) (2023-06-29)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.11...1.2.12)

**Implemented enhancements:**

- Implement incremental backoff and jitter [\#1156](https://github.com/ably/ably-dotnet/issues/1156)

**Closed issues:**

- in maui, android, getting nullreference exception when creating ablyRest and AblyRealtime in Release [\#1241](https://github.com/ably/ably-dotnet/issues/1241)
- Update git submodules to `main` branch [\#1234](https://github.com/ably/ably-dotnet/issues/1234)
- Add tests + doc for batch publish [\#1232](https://github.com/ably/ably-dotnet/issues/1232)
- Fix SDK version for unity package exporter [\#1228](https://github.com/ably/ably-dotnet/issues/1228)
- Implement RTN20C: Handle os connectivity event while `CONNECTING` [\#1218](https://github.com/ably/ably-dotnet/issues/1218)
- Update README with logging [\#1217](https://github.com/ably/ably-dotnet/issues/1217)
- Add unity specific agent header [\#1208](https://github.com/ably/ably-dotnet/issues/1208)
- Expunge references of 'ably.io' from URLs etc [\#1112](https://github.com/ably/ably-dotnet/issues/1112)

**Merged pull requests:**

- Fix README supported platforms + flaky tests [\#1236](https://github.com/ably/ably-dotnet/pull/1236) ([sacOO7](https://github.com/sacOO7))
- Update/git submodules [\#1235](https://github.com/ably/ably-dotnet/pull/1235) ([sacOO7](https://github.com/sacOO7))
- Fix ably io urls [\#1233](https://github.com/ably/ably-dotnet/pull/1233) ([sacOO7](https://github.com/sacOO7))
- Update doc+tests for batch publish [\#1230](https://github.com/ably/ably-dotnet/pull/1230) ([sacOO7](https://github.com/sacOO7))
- Handle connectivity event [\#1226](https://github.com/ably/ably-dotnet/pull/1226) ([sacOO7](https://github.com/sacOO7))
- Unity agent header [\#1220](https://github.com/ably/ably-dotnet/pull/1220) ([sacOO7](https://github.com/sacOO7))
- Add incremental backoff and jitter [\#1176](https://github.com/ably/ably-dotnet/pull/1176) ([sacOO7](https://github.com/sacOO7))

## [1.2.11](https://github.com/ably/ably-dotnet/tree/1.2.11) (2023-05-22)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.10...1.2.11)

**Implemented enhancements:**

- Support net6.0, net7.0 and MAUI [\#1215](https://github.com/ably/ably-dotnet/pull/1215) ([sacOO7](https://github.com/sacOO7))

**Fixed bugs:**

- Fix QueueMessages ClientOption [\#1219](https://github.com/ably/ably-dotnet/issues/1219)

**Closed issues:**

- Fix synchronization context deprecation [\#1209](https://github.com/ably/ably-dotnet/issues/1209)
- .Net MAUI app throws System.Net.NetworkInformation.NetworkInformationException when creating AblyRealtime object [\#1205](https://github.com/ably/ably-dotnet/issues/1205)
- Update README + Unity Documentation [\#1204](https://github.com/ably/ably-dotnet/issues/1204)
- Automate build and package for IO.Ably.PackagePush.sln [\#1202](https://github.com/ably/ably-dotnet/issues/1202)
- Support for .NET Multi-platform App UI \(.NET MAUI\) [\#1178](https://github.com/ably/ably-dotnet/issues/1178))
- Update release notes with .NET 6 status [\#1116](https://github.com/ably/ably-dotnet/issues/1116)

**Merged pull requests:**

- Add logging doc [\#1225](https://github.com/ably/ably-dotnet/pull/1225) ([sacOO7](https://github.com/sacOO7))
- Fix QueueMessages when set to false [\#1216](https://github.com/ably/ably-dotnet/pull/1216) ([sacOO7](https://github.com/sacOO7))
- Support customContext  [\#1210](https://github.com/ably/ably-dotnet/pull/1210) ([sacOO7](https://github.com/sacOO7))
- Update README and unity doc [\#1207](https://github.com/ably/ably-dotnet/pull/1207) ([sacOO7](https://github.com/sacOO7))
- Fix release process and package publish [\#1203](https://github.com/ably/ably-dotnet/pull/1203) ([sacOO7](https://github.com/sacOO7))
- Unity documentation [\#1124](https://github.com/ably/ably-dotnet/pull/1124) ([sacOO7](https://github.com/sacOO7))

## [1.2.10](https://github.com/ably/ably-dotnet/tree/1.2.10) (2023-01-24)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.9...1.2.10)

**Closed issues:**
- Implement RSC7d \(Ably-Agent header\) [\#498](https://github.com/ably/ably-dotnet/issues/498)
- AuthCallback returned a string which can't be converted to TokenRequest [\#1196](https://github.com/ably/ably-dotnet/issues/1196)
- Unable to output Debug Logging to console [\#1182](https://github.com/ably/ably-dotnet/issues/1182)
- Unity iOS: Errors while creating transport [\#764](https://github.com/ably/ably-dotnet/issues/764)
- Unity: When building for iOS, an IL2CPP error prevents the app working [\#751](https://github.com/ably/ably-dotnet/issues/751)
- Unity 2019.3.5 - System.EntryPointNotFoundException: CreateNLSocket [\#408](https://github.com/ably/ably-dotnet/issues/408)

**Merged pull requests:**

- Fix authCallback token string [\#1197](https://github.com/ably/ably-dotnet/pull/1197) ([sacOO7](https://github.com/sacOO7))
- Feature/agent header [\#1195](https://github.com/ably/ably-dotnet/pull/1195) ([sacOO7](https://github.com/sacOO7))

## [1.2.9](https://github.com/ably/ably-dotnet/tree/1.2.9) (2022-08-31)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.8...1.2.9)

**Implemented enhancements:**

- Add support to get channel lifecycle status [\#1150](https://github.com/ably/ably-dotnet/issues/1150)

**Fixed bugs:**

- ClientOptions LogLevel not working [\#1140](https://github.com/ably/ably-dotnet/issues/1140)

**Closed issues:**

- Unity: Investigate inability to select GitHub as SCM [\#1173](https://github.com/ably/ably-dotnet/issues/1173)
- MacOS build fails when restoring packages [\#1160](https://github.com/ably/ably-dotnet/issues/1160)
- Remove support for Windows 7 [\#1158](https://github.com/ably/ably-dotnet/issues/1158)
- dotnet Update urls in readme [\#1146](https://github.com/ably/ably-dotnet/issues/1146)
- Skipped Test: ConnectionSandboxTransportSideEffectsSpecs.WithChannelInDetachingState\_WhenTransportIsDisconnected\_ShouldResendDetachMessageOnConnectionResumed [\#834](https://github.com/ably/ably-dotnet/issues/834)
- Dropping the `.NET Framework` solution [\#618](https://github.com/ably/ably-dotnet/issues/618)
- Take control of our Assembly Version [\#617](https://github.com/ably/ably-dotnet/issues/617)
- Update documentation about using alternative websocket libraries [\#425](https://github.com/ably/ably-dotnet/issues/425)

**Merged pull requests:**

- Contributing Guide [\#1179](https://github.com/ably/ably-dotnet/pull/1179) ([QuintinWillison](https://github.com/QuintinWillison))
- Add channel lifecycle support [\#1172](https://github.com/ably/ably-dotnet/pull/1172) ([tomkirbygreen](https://github.com/tomkirbygreen))
- Remove mention of Visual Studio 2017 [\#1169](https://github.com/ably/ably-dotnet/pull/1169) ([tomkirbygreen](https://github.com/tomkirbygreen))
- Remove support for 'Windows 7'. [\#1159](https://github.com/ably/ably-dotnet/pull/1159) ([tomkirbygreen](https://github.com/tomkirbygreen))
- Add message extras to the Message equality comparison [\#1157](https://github.com/ably/ably-dotnet/pull/1157) ([marto83](https://github.com/marto83))
- Update documentation URLs [\#1149](https://github.com/ably/ably-dotnet/pull/1149) ([tomkirbygreen](https://github.com/tomkirbygreen))
- Fix realtime logging [\#1139](https://github.com/ably/ably-dotnet/pull/1139) ([sacOO7](https://github.com/sacOO7))
- Resend detach on reconnect [\#1109](https://github.com/ably/ably-dotnet/pull/1109) ([sacOO7](https://github.com/sacOO7))

## [1.2.8](https://github.com/ably/ably-dotnet/tree/1.2.8) (2022-04-21)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.7...1.2.8)

**Implemented enhancements:**

- [Unity](https://unity.com/) Beta 2 release. Added example chat app (`unity/Assets/Ably/Examples/Chat`) along with `Edit` and `Play` mode unit tests (`unity/Assets/Tests`).

**Merged pull requests:**

- Remove the deprecated 'LogHander' property [\#1144](https://github.com/ably/ably-dotnet/pull/1144) ([tomkirbygreen](https://github.com/tomkirbygreen))
- Remove the Windows Phone 8 platform [\#1142](https://github.com/ably/ably-dotnet/pull/1142) ([tomkirbygreen](https://github.com/tomkirbygreen))
- Increase .NET Framework min ver from 4.6.1 to 4.6.2 [\#1127](https://github.com/ably/ably-dotnet/pull/1127) ([tomkirbygreen](https://github.com/tomkirbygreen))

## [1.2.7](https://github.com/ably/ably-dotnet/tree/1.2.7)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.6...1.2.7)

**Implemented enhancements:**

- First release of [Push Notifications](PushNotifications.md) support for iOS and Android devices, please refer to [PushNotifications.md](PushNotifications.md) for documentation.

**Merged pull requests:**

- Fix package build [\#1102](https://github.com/ably/ably-dotnet/pull/1102) ([marto83](https://github.com/marto83))
- Remove UWP as a specific platform [\#1101](https://github.com/ably/ably-dotnet/pull/1101) ([tomkirbygreen](https://github.com/tomkirbygreen))

## [1.2.6](https://github.com/ably/ably-dotnet/tree/1.2.6) (2022-01-07)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.5...1.2.6)

**Implemented enhancements:**

- Additional code quality fixes (see [Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.5...1.2.6))

**Fixed bugs:**

- Call to Release \(and ReleaseAll\) does not impact channels in RealtimeChannels [\#980](https://github.com/ably/ably-dotnet/issues/980)

## [1.2.5](https://github.com/ably/ably-dotnet/tree/1.2.5) (2021-07-02)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.4...1.2.5)

**Implemented enhancements:**

- Heartbeat support for detecting dropped connections [\#507](https://github.com/ably/ably-dotnet/issues/507)
- Numerous code quality fixes (see [Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.4...1.2.5))

## [1.2.4](https://github.com/ably/ably-dotnet/tree/1.2.4) (2021-05-05)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.3...1.2.4)

**Implemented enhancements:**
​
- Defaults: Generate environment fallbacks [\#438](https://github.com/ably/ably-dotnet/issues/438)
- Add idempotent REST publishing [\#237](https://github.com/ably/ably-dotnet/issues/237)
​
**Fixed bugs:**
​
- Potentially insecure usage of the NSURLSession API [\#487](https://github.com/ably/ably-dotnet/issues/487)
- Common crash related to websocket failing with error [\#486](https://github.com/ably/ably-dotnet/issues/486)
- Enterprise customer seeing crashes on iOS, Ably version 1.2.1 [\#485](https://github.com/ably/ably-dotnet/issues/485)
- Incorrect import reference? [\#484](https://github.com/ably/ably-dotnet/issues/484)
- SDK has no documented way to block on initialisation. [\#455](https://github.com/ably/ably-dotnet/issues/455)
​
**Closed issues:**
​
- Allow custom Transport Params \(RTC1f\) and \(RTC1f1\) [\#452](https://github.com/ably/ably-dotnet/issues/452)
- Deprecate history methods taking untilAttach parameter [\#449](https://github.com/ably/ably-dotnet/issues/449)
​
**Merged pull requests:**
​
- Bump System.Net.Http from 4.3.2 to 4.3.4 in /examples/AndroidSample [\#504](https://github.com/ably/ably-dotnet/pull/504) ([dependabot[bot]](https://github.com/apps/dependabot))
- Bump System.Net.Http from 4.3.2 to 4.3.4 in /src/IO.Ably.Tests [\#503](https://github.com/ably/ably-dotnet/pull/503) ([dependabot[bot]](https://github.com/apps/dependabot))
- Bump System.Net.Http from 4.3.3 to 4.3.4 in /src/IO.Ably.Tests.DotNetCore20 [\#502](https://github.com/ably/ably-dotnet/pull/502) ([dependabot[bot]](https://github.com/apps/dependabot))
- Bump System.Net.Security from 4.3.0 to 4.3.1 in /src/IO.Ably.Android [\#497](https://github.com/ably/ably-dotnet/pull/497) ([dependabot[bot]](https://github.com/apps/dependabot))
- Add ConnectionKey property to messages [\#500](https://github.com/ably/ably-dotnet/pull/500) ([jasonmsmith](https://github.com/jasonmsmith))
- Conform license and copyright [\#496](https://github.com/ably/ably-dotnet/pull/496) ([QuintinWillison](https://github.com/QuintinWillison))
- Add maintainers file [\#495](https://github.com/ably/ably-dotnet/pull/495) ([niksilver](https://github.com/niksilver))
- Deprecate Presence history methods [\#488](https://github.com/ably/ably-dotnet/pull/488) ([sacOO7](https://github.com/sacOO7))
- Console app documentation [\#461](https://github.com/ably/ably-dotnet/pull/461) ([sacOO7](https://github.com/sacOO7))
- Idempotent Rest Publish [\#457](https://github.com/ably/ably-dotnet/pull/457) ([sacOO7](https://github.com/sacOO7))
- Fix client time drift [\#456](https://github.com/ably/ably-dotnet/pull/456) ([sacOO7](https://github.com/sacOO7))
- Generate environment fallbacks [\#454](https://github.com/ably/ably-dotnet/pull/454) ([sacOO7](https://github.com/sacOO7))

## [1.2.3](https://github.com/ably/ably-dotnet/tree/1.2.3) (2020-11-03)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.2...1.2.3)

**Implemented enhancements:**

- Custom transport params [\#452](https://github.com/ably/ably-dotnet/issues/452)

**Fixed bugs:**

- Websocket failures during high load [\#446](https://github.com/ably/ably-dotnet/issues/446)

**Closed issues:**

- Migrate CI to Github actions [\#442](https://github.com/ably/ably-dotnet/issues/442)
- \[Cleanup\] Remove unused/unnecessary tools and files [\#434](https://github.com/ably/ably-dotnet/issues/434)

**Merged pull requests:**

- Feature/452 transport params [\#453](https://github.com/ably/ably-dotnet/pull/453) ([marto83](https://github.com/marto83))
- Issue/446 websocket trouble [\#451](https://github.com/ably/ably-dotnet/pull/451) ([marto83](https://github.com/marto83))
- Issue/434 cleanup [\#450](https://github.com/ably/ably-dotnet/pull/450) ([marto83](https://github.com/marto83))
- Github workflow [\#445](https://github.com/ably/ably-dotnet/pull/445) ([marto83](https://github.com/marto83))

## [1.2.2](https://github.com/ably/ably-dotnet/tree/1.2.2) (2020-09-24)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.1...1.2.2)

The release includes mainly fixes and improvements

**Breaking change**

- Update `ITransport.Send` return type from `void` to `Result` so it can indicate to the library whether the send succeeded or failed immediately.
  It helped improve a race condition where the transport was disconnected but the library had not processed the update yet. Now we handle queuing and resending messages much better. 

**Implemented enhancements:**

- Improve handling of clock skew [\#303](https://github.com/ably/ably-dotnet/issues/303)

**Fixed bugs:**

- Failing internet check [\#437](https://github.com/ably/ably-dotnet/issues/437)

**Closed issues:**

- \[CI\] Fix build and tests [\#435](https://github.com/ably/ably-dotnet/issues/435)
- Typo in property name for log handler [\#427](https://github.com/ably/ably-dotnet/issues/427)
- MessageExtras to comply with TM2i [\#422](https://github.com/ably/ably-dotnet/issues/422)

**Merged pull requests:**

- Issue/435 fix build and tests [\#443](https://github.com/ably/ably-dotnet/pull/443) ([marto83](https://github.com/marto83))
- Issue/437 [\#440](https://github.com/ably/ably-dotnet/pull/440) ([marto83](https://github.com/marto83))
- Fix auth token clock-skew [\#439](https://github.com/ably/ably-dotnet/pull/439) ([sacOO7](https://github.com/sacOO7))
- Refactor MessageExtras [\#433](https://github.com/ably/ably-dotnet/pull/433) ([sacOO7](https://github.com/sacOO7))
- Fix Property Typo [\#432](https://github.com/ably/ably-dotnet/pull/432) ([sacOO7](https://github.com/sacOO7))
- Remove psake tool [\#431](https://github.com/ably/ably-dotnet/pull/431) ([sacOO7](https://github.com/sacOO7))
- Rename master to main [\#430](https://github.com/ably/ably-dotnet/pull/430) ([QuintinWillison](https://github.com/QuintinWillison))
- Feature/tm2i message extras [\#424](https://github.com/ably/ably-dotnet/pull/424) ([marto83](https://github.com/marto83))
- Update build to copy all required dlls when packaging [\#420](https://github.com/ably/ably-dotnet/pull/420) ([marto83](https://github.com/marto83))

## [1.2.1](https://github.com/ably/ably-dotnet/tree/1.2.1) (2020-06-12)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.2.0...1.2.1)

Fixes packaging issue for .NET Framework, Xamarin.iOS and Xamarin.Android

- Close issue \#419. Failed to resolve IO.Ably.DeltaCodec [\#419](https://github.com/ably/ably-dotnet/issues/419) ([marto83](https://github.com/marto83))

## [1.2.0](https://github.com/ably/ably-dotnet/tree/1.2.0) (2020-06-08)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.20...1.2.0)

Adds the capability to subscribe to a channel in delta mode.

Subscribing to a channel in delta mode enables [delta compression](https://www.ably.io/documentation/realtime/channels/channel-parameters/deltas). This is a way for a client to subscribe to a channel so that message payloads sent contain only the difference (ie the delta) between the present message and the previous message on the channel.

## [1.1.20](https://github.com/ably/ably-dotnet/tree/1.1.20) (2020-04-24)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.19...1.1.20)

**Closed issues:**

- Signed version of the .Net Standard 2.x assembly [\#412](https://github.com/ably/ably-dotnet/issues/412)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.18...1.1.19)

**Fixed bugs:**

- Unexpected error: The current state \[Suspended\] does not allow messages to be sent [\#409](https://github.com/ably/ably-dotnet/issues/409)

**Merged pull requests:**

- Close issue \#409. Call error callback instead of through exception wh… [\#410](https://github.com/ably/ably-dotnet/pull/410) ([marto83](https://github.com/marto83))

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.17...1.1.18)

**Fixed bugs:**

- Cumulative retry timeout of 10s was exceeded [\#404](https://github.com/ably/ably-dotnet/issues/404)
- Connection moves to Failed when there is no internet connection [\#402](https://github.com/ably/ably-dotnet/issues/402)
- Could not load type of field IO.Ably.Transport.ConnectionManager [\#384](https://github.com/ably/ably-dotnet/issues/384)

**Closed issues:**

- Error thrown when trying to initialize Ably object in Unity [\#371](https://github.com/ably/ably-dotnet/issues/371)

**Merged pull requests:**

- Issues/404 [\#405](https://github.com/ably/ably-dotnet/pull/405) ([marto83](https://github.com/marto83))
- Issues/402 [\#403](https://github.com/ably/ably-dotnet/pull/403) ([marto83](https://github.com/marto83))
- Issue/384 [\#401](https://github.com/ably/ably-dotnet/pull/401) ([marto83](https://github.com/marto83))

## [1.1.17](https://github.com/ably/ably-dotnet/tree/1.1.17) (2020-02-17)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.16...1.1.17)

**Fixed bugs:**

- Don't throw exceptions when there is a callback passed [\#397](https://github.com/ably/ably-dotnet/issues/397)

**Closed issues:**

- Remove develop branch [\#393](https://github.com/ably/ably-dotnet/issues/393)

**Merged pull requests:**

- Fix \#397. Call callback instead of throwing AblyException [\#398](https://github.com/ably/ably-dotnet/pull/398) ([marto83](https://github.com/marto83))

## [1.1.16](https://github.com/ably/ably-dotnet/tree/1.1.16) (2020-01-31)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.15...1.1.16)

**Implemented enhancements:**

- Expose a custom way to notify the library about OperatingSystemNetworkState [\#380](https://github.com/ably/ably-dotnet/issues/380)

**Merged pull requests:**

- Issue/380 allow to disable networkstate monitoring [\#392](https://github.com/ably/ably-dotnet/pull/392) ([marto83](https://github.com/marto83))


## [1.1.15](https://github.com/ably/ably-dotnet/tree/1.1.15) (2020-01-10)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.15-beta1...1.1.15)

**Implemented enhancements:**

- Add test for RTC8a4 [\#316](https://github.com/ably/ably-dotnet/issues/316)
- Add support for remembered REST fallback host  [\#268](https://github.com/ably/ably-dotnet/issues/268)
- Include the host in error messages for v1.0 [\#218](https://github.com/ably/ably-dotnet/issues/218)

**Fixed bugs:**

- On unhandled exception should not put channel into failed state [\#378](https://github.com/ably/ably-dotnet/issues/378)
- Xamaring Android when full linking is enabled fails to request token [\#375](https://github.com/ably/ably-dotnet/issues/375)
- Disconnect event not fired and will not connect after explicit disconnect [\#360](https://github.com/ably/ably-dotnet/issues/360)
- Odd API for untilAttach queries [\#100](https://github.com/ably/ably-dotnet/issues/100)

**Closed issues:**

- Application freezes on network state change [\#373](https://github.com/ably/ably-dotnet/issues/373)
- Remove System.Net.Http nuget dependency from full .Net Framework build [\#211](https://github.com/ably/ably-dotnet/issues/211)

**Merged pull requests:**

- Release/1.1.15 [\#390](https://github.com/ably/ably-dotnet/pull/390) ([marto83](https://github.com/marto83))
- Bump Microsoft.NETCore.UniversalWindowsPlatform from 6.0.2 to 6.0.6 in /src/IO.Ably.UWP [\#388](https://github.com/ably/ably-dotnet/pull/388) ([dependabot[bot]](https://github.com/apps/dependabot))
- Implemented RSC15f and RTN17e and improved fallback handling [\#385](https://github.com/ably/ably-dotnet/pull/385) ([marto83](https://github.com/marto83))
- Feature/spec align [\#383](https://github.com/ably/ably-dotnet/pull/383) ([marto83](https://github.com/marto83))

## [1.1.15-beta1](https://github.com/ably/ably-dotnet/tree/1.1.15-beta1)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.14...1.1.15-beta1)

**Implemented enhancements:**

- Improve library internals [\#363](https://github.com/ably/ably-dotnet/issues/363)

**Fixed bugs:**

- Handle Presence Sync with empty list of presence messages [\#379](https://github.com/ably/ably-dotnet/issues/379)
- Using a clientId should no longer be forcing token auth in the 1.1 spec [\#330](https://github.com/ably/ably-dotnet/issues/330)

**Closed issues:**

- Resuming after Windows has been asleep \(.Net Framework\) [\#332](https://github.com/ably/ably-dotnet/issues/332)
- Detecting network availability changes [\#331](https://github.com/ably/ably-dotnet/issues/331)

**Merged pull requests:**

- Poc/channels [\#372](https://github.com/ably/ably-dotnet/pull/372) ([marto83](https://github.com/marto83))


## [1.1.14](https://github.com/ably/ably-dotnet/tree/1.1.14)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.13...1.1.14)

**Implemented enhancements:**

- TM3 Improvements [\#369](https://github.com/ably/ably-dotnet/issues/369)

**Fixed bugs:**

- Token Expiry error when QueryTime is set to true [\#374](https://github.com/ably/ably-dotnet/issues/374)

**Closed issues:**

- IO.Ably.AblyAuth RequestTokenAsync timeout exception not catchable [\#366](https://github.com/ably/ably-dotnet/issues/366)

**Merged pull requests:**

- Fix issue where incorrect time was used to check Token validity [\#376](https://github.com/ably/ably-dotnet/pull/376) ([marto83](https://github.com/marto83))
- Add fromEncoded and fromEncodedArray that accept json string [\#370](https://github.com/ably/ably-dotnet/pull/370) ([marto83](https://github.com/marto83))
- ClientId in options should not force token auth  \(RSA7e2\) [\#357](https://github.com/ably/ably-dotnet/pull/357) ([withakay](https://github.com/withakay))

## [1.1.13](https://github.com/ably/ably-dotnet/tree/1.1.13)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.12...1.1.13)

**Implemented enhancements:**

- Ensure request method accepts UPDATE, PATCH & DELETE verbs [\#246](https://github.com/ably/ably-dotnet/issues/246)
- Disable fallback hosts option - hot fix [\#216](https://github.com/ably/ably-dotnet/issues/216)
- Integrate a Linter/Code formatting tool into the build process [\#179](https://github.com/ably/ably-dotnet/issues/179)
- Ably dll and iOS app size increased [\#134](https://github.com/ably/ably-dotnet/issues/134)
- Ability to set the JsonSerializerSettings [\#92](https://github.com/ably/ably-dotnet/issues/92)
- Realtime: Presence [\#8](https://github.com/ably/ably-dotnet/issues/8)

**Fixed bugs:**

- Error from authUrl should give some indication that it's from the authUrl [\#252](https://github.com/ably/ably-dotnet/issues/252)
- AttachAsync\(\) doesn't timeout on connection state changes [\#117](https://github.com/ably/ably-dotnet/issues/117)

**Closed issues:**

- Channel didn't attach within the default timeout; Code: 50000, Intermittent [\#115](https://github.com/ably/ably-dotnet/issues/115)
- Update docs about using with Xamarin [\#77](https://github.com/ably/ably-dotnet/issues/77)

**Merged pull requests:**

- RunInBackground bug [\#367](https://github.com/ably/ably-dotnet/pull/367) ([withakay](https://github.com/withakay))

## [1.1.12](https://github.com/ably/ably-dotnet/tree/1.1.12)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.11...1.1.12)

**Fixed bugs:**

- Presence re-entry requirement change for 1.1 [\#361](https://github.com/ably/ably-dotnet/issues/361)
- Presence not entered on recovered connection [\#329](https://github.com/ably/ably-dotnet/issues/329)
- Investigate report of PublishAsync hanging/timing out [\#314](https://github.com/ably/ably-dotnet/issues/314)

**Merged pull requests:**

- Reenter after short disconnect [\#358](https://github.com/ably/ably-dotnet/pull/358) ([withakay](https://github.com/withakay))

## [1.1.10](https://github.com/ably/ably-dotnet/tree/1.1.10)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.9-beta1...1.1.10)

**Fixed bugs:**

- history.NextAsync\(\) throws 40000 exception [\#352](https://github.com/ably/ably-dotnet/issues/352)
- Ably 1.1.8 - The type initializer for 'IO.Ably.Defaults' threw an exception. [\#349](https://github.com/ably/ably-dotnet/issues/349)
- Crash in 1.1.6 on iOS & Android with Xamarin [\#346](https://github.com/ably/ably-dotnet/issues/346)

**Merged pull requests:**

- split link header values on comma to handle platform implementation differences [\#353](https://github.com/ably/ably-dotnet/pull/353) ([withakay](https://github.com/withakay))
- Presence not re-entering after connect disconnect fix [\#350](https://github.com/ably/ably-dotnet/pull/350) ([withakay](https://github.com/withakay))

## [1.1.9-beta1](https://github.com/ably/ably-dotnet/tree/1.1.9-beta1)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.8...1.1.9-beta1)

**Proposed pull requests:**

https://github.com/ably/ably-dotnet/pull/350

## [1.1.8](https://github.com/ably/ably-dotnet/tree/1.1.8)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.6...1.1.7)

**Fixed bugs:**

- NRE in IO.Ably.Transport.MsWebSocketTransport.ConnectAndStartListening and SemaphoreSlim.Wait [\#334](https://github.com/ably/ably-dotnet/issues/334)

**Merged pull requests:**

- Fix for issue \#346  [\#347](https://github.com/ably/ably-dotnet/pull/347) ([withakay](https://github.com/withakay))

## [1.1.6](https://github.com/ably/ably-dotnet/tree/1.1.6)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.5...1.1.6)

**Implemented enhancements:**

- Idempotent publishing is not enabled in the upcoming 1.1 release [\#275](https://github.com/ably/ably-dotnet/issues/275)
- Unity support [\#169](https://github.com/ably/ably-dotnet/issues/169)

**Fixed bugs:**

- JsonReader error on Xamarin [\#325](https://github.com/ably/ably-dotnet/issues/325)

**Closed issues:**

- NRE when switching networks [\#200](https://github.com/ably/ably-dotnet/issues/200)
- Prefer the American English spelling for Initialise\(d\) [\#191](https://github.com/ably/ably-dotnet/issues/191)

**Merged pull requests:**

- Transient Publishing [\#343](https://github.com/ably/ably-dotnet/pull/343) ([withakay](https://github.com/withakay))

## [1.1.5](https://github.com/ably/ably-dotnet/tree/1.1.5)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.4...1.1.5)

**Merged pull requests:**

- PublishAsync improvement [\#341](https://github.com/ably/ably-dotnet/pull/341) ([withakay](https://github.com/withakay))

## [1.1.4](https://github.com/ably/ably-dotnet/tree/1.1.4)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.3...1.1.4)

**Merged pull requests:**

- Dispose guarding [\#339](https://github.com/ably/ably-dotnet/pull/339) ([withakay](https://github.com/withakay))
- Issue 334 follow up [\#338](https://github.com/ably/ably-dotnet/pull/338) ([withakay](https://github.com/withakay))

## [1.1.3](https://github.com/ably/ably-dotnet/tree/1.1.3)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.2...1.1.3)

**Implemented enhancements:**

- Add test for JWT token [\#210](https://github.com/ably/ably-dotnet/issues/210)

**Fixed bugs:**

- Collection was modified in ConnectionManager [\#267](https://github.com/ably/ably-dotnet/issues/267)
- Socket is null in MsWebSocketTransport [\#264](https://github.com/ably/ably-dotnet/issues/264)

**Merged pull requests:**

- Fix for Issue 334, incorrectly disposing of a blocking collection [\#335](https://github.com/ably/ably-dotnet/pull/335) ([withakay](https://github.com/withakay))
- Add InnerException to ErrorInfo [\#328](https://github.com/ably/ably-dotnet/pull/328) ([withakay](https://github.com/withakay))
- Idempotent defaults to off for version \< 1.2 [\#327](https://github.com/ably/ably-dotnet/pull/327) ([withakay](https://github.com/withakay))
- update xunit and moq to latest versions [\#326](https://github.com/ably/ably-dotnet/pull/326) ([withakay](https://github.com/withakay))

## [1.1.2](https://github.com/ably/ably-dotnet/tree/1.1.2)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.1...1.1.2)

**Implemented enhancements:**

- Clean up Log.Debug calls [\#229](https://github.com/ably/ably-dotnet/issues/229)

**Closed issues:**

- presence myMembers map implementation [\#302](https://github.com/ably/ably-dotnet/issues/302)
- Test should verify message was processed [\#299](https://github.com/ably/ably-dotnet/issues/299)
- Presence \(v1.1\) if QueueMessages = false should raise an error [\#298](https://github.com/ably/ably-dotnet/issues/298)
- Unnecessary disconnect/connect cycle on token authentication [\#276](https://github.com/ably/ably-dotnet/issues/276)

**Merged pull requests:**

- HistoryRequestParams fix [\#323](https://github.com/ably/ably-dotnet/pull/323) ([withakay](https://github.com/withakay))
- Code cleanup [\#322](https://github.com/ably/ably-dotnet/pull/322) ([withakay](https://github.com/withakay))

## [1.1.1](https://github.com/ably/ably-dotnet/tree/1.1.1)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.0...1.1.1)

**Merged pull requests:**

- Fix for issue 313 [\#318](https://github.com/ably/ably-dotnet/pull/318) ([withakay](https://github.com/withakay))

**Fixed bugs:**

- Thread leak through disconnect/resume sequence [\#313](https://github.com/ably/ably-dotnet/issues/313)

## [1.1.0](https://github.com/ably/ably-dotnet/tree/1.1.0)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/1.1.0-beta1...1.1.0)

**Closed issues:**

- Release nugget still shows 0.8.11 [\#304](https://github.com/ably/ably-dotnet/issues/304)

**Merged pull requests:**

- RTL11 test regression fix [\#315](https://github.com/ably/ably-dotnet/pull/315) ([withakay](https://github.com/withakay))
- RTP16b fix [\#312](https://github.com/ably/ably-dotnet/pull/312) ([withakay](https://github.com/withakay))
- Fix for issue \#299.  [\#311](https://github.com/ably/ably-dotnet/pull/311) ([withakay](https://github.com/withakay))
- RTP17b test identification [\#310](https://github.com/ably/ably-dotnet/pull/310) ([withakay](https://github.com/withakay))
- TM3 [\#309](https://github.com/ably/ably-dotnet/pull/309) ([withakay](https://github.com/withakay))
- RTL15/a [\#308](https://github.com/ably/ably-dotnet/pull/308) ([withakay](https://github.com/withakay))
- modifications to ErrorInfo to meet TI4 & TI5 spec [\#307](https://github.com/ably/ably-dotnet/pull/307) ([withakay](https://github.com/withakay))
- JWT support [\#306](https://github.com/ably/ably-dotnet/pull/306) ([withakay](https://github.com/withakay))
- fix test for idempotent rest publishing for version 1.1 of spec. [\#305](https://github.com/ably/ably-dotnet/pull/305) ([withakay](https://github.com/withakay))
- RSA8e, RSA9h and RSA10j [\#301](https://github.com/ably/ably-dotnet/pull/301) ([withakay](https://github.com/withakay))

## [1.1.0-beta1](https://github.com/ably/ably-dotnet/tree/1.1.0-beta1)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.11...1.1.0-beta1)

**Fixed bugs:**

- Channel apparently successfully attaches, but OnAttachTimeout fires anyway [\#205](https://github.com/ably/ably-dotnet/issues/205)

**Closed issues:**

- .NET Core 2.0 confusion [\#266](https://github.com/ably/ably-dotnet/issues/266)
- Should the AblyRest class be an injected singleton? [\#260](https://github.com/ably/ably-dotnet/issues/260)
- readme: use authcallback, don't instantiate the lib with literal token [\#253](https://github.com/ably/ably-dotnet/issues/253)
- IO.Ably.Realtime.RealtimeChannel.PublishImpl [\#251](https://github.com/ably/ably-dotnet/issues/251)
- support for windows 7 in ably 0.8.11 [\#247](https://github.com/ably/ably-dotnet/issues/247)
- Presence [\#235](https://github.com/ably/ably-dotnet/issues/235)
- Presence sync doesn't complete on the first chance [\#204](https://github.com/ably/ably-dotnet/issues/204)
- There is already one outstanding 'SendAsync' call for this WebSocket instance  [\#101](https://github.com/ably/ably-dotnet/issues/101)

**Merged pull requests:**

- v1.1 release [\#300](https://github.com/ably/ably-dotnet/pull/300) ([withakay](https://github.com/withakay))
- Known limitations section in README [\#297](https://github.com/ably/ably-dotnet/pull/297) ([Srushtika](https://github.com/Srushtika))
- ChannelStateChange \(TH\) [\#296](https://github.com/ably/ably-dotnet/pull/296) ([withakay](https://github.com/withakay))
- RTL13a, RTL13b, RTL13c and RTL4f [\#295](https://github.com/ably/ably-dotnet/pull/295) ([withakay](https://github.com/withakay))
- RTL6c5 [\#294](https://github.com/ably/ably-dotnet/pull/294) ([withakay](https://github.com/withakay))
- RTN16b and RTN16f [\#293](https://github.com/ably/ably-dotnet/pull/293) ([withakay](https://github.com/withakay))
- RTN15g and subsections [\#292](https://github.com/ably/ably-dotnet/pull/292) ([withakay](https://github.com/withakay))
- RTN15h test fix [\#291](https://github.com/ably/ably-dotnet/pull/291) ([withakay](https://github.com/withakay))
- RTN15c5 [\#289](https://github.com/ably/ably-dotnet/pull/289) ([withakay](https://github.com/withakay))
- RTN15c4 [\#288](https://github.com/ably/ably-dotnet/pull/288) ([withakay](https://github.com/withakay))
- RTN15c3 [\#287](https://github.com/ably/ably-dotnet/pull/287) ([withakay](https://github.com/withakay))
- RTN15c2 [\#286](https://github.com/ably/ably-dotnet/pull/286) ([withakay](https://github.com/withakay))
- RTN15c1 [\#285](https://github.com/ably/ably-dotnet/pull/285) ([withakay](https://github.com/withakay))
- RTP16c [\#283](https://github.com/ably/ably-dotnet/pull/283) ([withakay](https://github.com/withakay))
- RTP16b [\#282](https://github.com/ably/ably-dotnet/pull/282) ([withakay](https://github.com/withakay))
- Test for RTP16a [\#281](https://github.com/ably/ably-dotnet/pull/281) ([withakay](https://github.com/withakay))
- Test cover for RTP5c3 [\#280](https://github.com/ably/ably-dotnet/pull/280) ([withakay](https://github.com/withakay))
- Tests for RTP5c2 [\#279](https://github.com/ably/ably-dotnet/pull/279) ([withakay](https://github.com/withakay))
- Test for RTP2e [\#278](https://github.com/ably/ably-dotnet/pull/278) ([withakay](https://github.com/withakay))
- Notes on how to reference a project from source. [\#277](https://github.com/ably/ably-dotnet/pull/277) ([withakay](https://github.com/withakay))
- Unity compatibility update [\#274](https://github.com/ably/ably-dotnet/pull/274) ([withakay](https://github.com/withakay))
- Merge RTL3c into RTN7c [\#273](https://github.com/ably/ably-dotnet/pull/273) ([withakay](https://github.com/withakay))
- Merge RTL3d into RTL3c [\#272](https://github.com/ably/ably-dotnet/pull/272) ([withakay](https://github.com/withakay))
- Rtn14a+rtn14b+rtn15h [\#271](https://github.com/ably/ably-dotnet/pull/271) ([withakay](https://github.com/withakay))
- Test tweaks for ci [\#270](https://github.com/ably/ably-dotnet/pull/270) ([withakay](https://github.com/withakay))
- RTP17 tests [\#269](https://github.com/ably/ably-dotnet/pull/269) ([withakay](https://github.com/withakay))
- RTP5a & RTP5b [\#265](https://github.com/ably/ably-dotnet/pull/265) ([withakay](https://github.com/withakay))
- properties should be placed before the constructor [\#263](https://github.com/ably/ably-dotnet/pull/263) ([withakay](https://github.com/withakay))
- RTP17 and RTP17a tests [\#262](https://github.com/ably/ably-dotnet/pull/262) ([withakay](https://github.com/withakay))
- RTP19, RTP19a and RTP6b [\#261](https://github.com/ably/ably-dotnet/pull/261) ([withakay](https://github.com/withakay))
- RTP11 and subsections [\#258](https://github.com/ably/ably-dotnet/pull/258) ([withakay](https://github.com/withakay))
- Test tweaks  [\#257](https://github.com/ably/ably-dotnet/pull/257) ([withakay](https://github.com/withakay))
- RTN19b [\#256](https://github.com/ably/ably-dotnet/pull/256) ([withakay](https://github.com/withakay))
- RTP3 [\#255](https://github.com/ably/ably-dotnet/pull/255) ([withakay](https://github.com/withakay))
- Update README with better AuthCallback example [\#254](https://github.com/ably/ably-dotnet/pull/254) ([withakay](https://github.com/withakay))
- ADded stylecop.json [\#250](https://github.com/ably/ably-dotnet/pull/250) ([withakay](https://github.com/withakay))
- RSL1k  - Idempotent rest publishing [\#249](https://github.com/ably/ably-dotnet/pull/249) ([withakay](https://github.com/withakay))
- Update README with not on PCLs [\#248](https://github.com/ably/ably-dotnet/pull/248) ([withakay](https://github.com/withakay))
- RTL2d tests [\#245](https://github.com/ably/ably-dotnet/pull/245) ([withakay](https://github.com/withakay))
- RTL11 & RTL11a [\#244](https://github.com/ably/ably-dotnet/pull/244) ([withakay](https://github.com/withakay))
- RTL2b, RTL2f, RTL2g, RTL12 & RTL3d [\#243](https://github.com/ably/ably-dotnet/pull/243) ([withakay](https://github.com/withakay))
- RTL3d [\#242](https://github.com/ably/ably-dotnet/pull/242) ([withakay](https://github.com/withakay))
- RTL3e [\#241](https://github.com/ably/ably-dotnet/pull/241) ([withakay](https://github.com/withakay))
- RTL3c [\#240](https://github.com/ably/ably-dotnet/pull/240) ([withakay](https://github.com/withakay))
- RTL2 Channel States and Events [\#239](https://github.com/ably/ably-dotnet/pull/239) ([withakay](https://github.com/withakay))
- RSC19 [\#238](https://github.com/ably/ably-dotnet/pull/238) ([withakay](https://github.com/withakay))
- Update README.md to cover .NET Core support [\#236](https://github.com/ably/ably-dotnet/pull/236) ([withakay](https://github.com/withakay))
- RTC8 and RTC8a subsections [\#234](https://github.com/ably/ably-dotnet/pull/234) ([withakay](https://github.com/withakay))
- RSA10a [\#233](https://github.com/ably/ably-dotnet/pull/233) ([withakay](https://github.com/withakay))
- RTN12d [\#232](https://github.com/ably/ably-dotnet/pull/232) ([withakay](https://github.com/withakay))
- RTN11b, RTN11c, RTN11d [\#230](https://github.com/ably/ably-dotnet/pull/230) ([withakay](https://github.com/withakay))
- RTN24, RTN21 and partial RTN4h [\#228](https://github.com/ably/ably-dotnet/pull/228) ([withakay](https://github.com/withakay))
- RTN22 and RTN22a [\#226](https://github.com/ably/ably-dotnet/pull/226) ([withakay](https://github.com/withakay))
- RTN7c Suspended [\#225](https://github.com/ably/ably-dotnet/pull/225) ([withakay](https://github.com/withakay))
- RSA4 small tweak [\#224](https://github.com/ably/ably-dotnet/pull/224) ([withakay](https://github.com/withakay))
- RTN15i [\#223](https://github.com/ably/ably-dotnet/pull/223) ([withakay](https://github.com/withakay))
- Release 0.8.11 [\#222](https://github.com/ably/ably-dotnet/pull/222) ([withakay](https://github.com/withakay))
- RTN14a, RTN14b, RTN15h [\#221](https://github.com/ably/ably-dotnet/pull/221) ([withakay](https://github.com/withakay))
- TR4 & AD1 [\#215](https://github.com/ably/ably-dotnet/pull/215) ([withakay](https://github.com/withakay))
- RTN4h, TA1, TA2, TA3, TA5 [\#214](https://github.com/ably/ably-dotnet/pull/214) ([withakay](https://github.com/withakay))
- RSA4c+d [\#212](https://github.com/ably/ably-dotnet/pull/212) ([withakay](https://github.com/withakay))
- AuthCallback return an AuthCallbackResult [\#203](https://github.com/ably/ably-dotnet/pull/203) ([withakay](https://github.com/withakay))
- RSA4a+b [\#201](https://github.com/ably/ably-dotnet/pull/201) ([withakay](https://github.com/withakay))


## [0.8.11](https://github.com/ably/ably-dotnet/tree/0.8.11)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.10...0.8.11)

**Fixed bugs:**

- Channel apparently successfully attaches, but OnAttachTimeout fires anyway [\#205](https://github.com/ably/ably-dotnet/issues/205)

**Merged pull requests:**

- Issue 205 [\#213](https://github.com/ably/ably-dotnet/pull/213) ([withakay](https://github.com/withakay))


## [0.8.10](https://github.com/ably/ably-dotnet/tree/0.8.10)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.9...0.8.10)

**Fixed bugs:**

- README is duplicated [\#184](https://github.com/ably/ably-dotnet/issues/184)
- Investigate report of System.Net.Http needing to be installed  [\#180](https://github.com/ably/ably-dotnet/issues/180)
- Use of task.ConfigureAwait\(false\) can give rise to out of order messages [\#143](https://github.com/ably/ably-dotnet/issues/143)
- 8.5 Android Presence+PresenceMap.EndSync\(\) [\#138](https://github.com/ably/ably-dotnet/issues/138)
- Channel state changes twice [\#125](https://github.com/ably/ably-dotnet/issues/125)
- App doesn't receive messages after a while [\#122](https://github.com/ably/ably-dotnet/issues/122)

**Closed issues:**

- Consider exposing the iOS and Android native SDKs as a NuGet package [\#202](https://github.com/ably/ably-dotnet/issues/202)
- Java.IO.IOException with Xamarin [\#172](https://github.com/ably/ably-dotnet/issues/172)

**Merged pull requests:**

- regex would only match single digit [\#220](https://github.com/ably/ably-dotnet/pull/220) ([withakay](https://github.com/withakay))
- Hotfix for issue 216 [\#217](https://github.com/ably/ably-dotnet/pull/217) ([withakay](https://github.com/withakay))
- updated Supported Platforms section in README [\#208](https://github.com/ably/ably-dotnet/pull/208) ([withakay](https://github.com/withakay))
- namespace was not updated when the project name was updated [\#207](https://github.com/ably/ably-dotnet/pull/207) ([withakay](https://github.com/withakay))
- Fix delay in initial presence sync [\#206](https://github.com/ably/ably-dotnet/pull/206) ([ashikns](https://github.com/ashikns))
- Deduplicated the README and merged edits between the two halves [\#185](https://github.com/ably/ably-dotnet/pull/185) ([withakay](https://github.com/withakay))

## [0.8.9](https://github.com/ably/ably-dotnet/tree/0.8.9)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.8...0.8.9)

**Closed issues:**

- v.87 The current state \[Suspended\] does not allow messages to be sent.; Code: 500 [\#171](https://github.com/ably/ably-dotnet/issues/171)

**Merged pull requests:**

- Update package deps [\#181](https://github.com/ably/ably-dotnet/pull/181) ([withakay](https://github.com/withakay))


## [0.8.8](https://github.com/ably/ably-dotnet/tree/0.8.8) (2018-02-06)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.7...0.8.8)

**Fixed bugs:**

- Channel options consistently being overridden on realtime channels \(v0.8.6\) [\#167](https://github.com/ably/ably-dotnet/issues/167)
- 0.8.4.8 - Unable to publish in detached or failed state [\#135](https://github.com/ably/ably-dotnet/issues/135)

**Merged pull requests:**

- Null check WebSocket Client [\#177](https://github.com/ably/ably-dotnet/pull/177) ([withakay](https://github.com/withakay))
- Circleci [\#176](https://github.com/ably/ably-dotnet/pull/176) ([withakay](https://github.com/withakay))
- Test and ci improvements [\#175](https://github.com/ably/ably-dotnet/pull/175) ([withakay](https://github.com/withakay))
- Use a shared project for tests to facilitate cross platform testing. [\#174](https://github.com/ably/ably-dotnet/pull/174) ([withakay](https://github.com/withakay))

## [0.8.7](https://github.com/ably/ably-dotnet/tree/0.8.6) (2017-12-22)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.7...HEAD)

**Implemented enhancements:**

- Add reauth capability [\#63](https://github.com/ably/ably-dotnet/issues/63)

**Fixed bugs:**

- After upgrade to 0.8.6, System.Net.WebSockets.WebSocketException [\#166](https://github.com/ably/ably-dotnet/issues/166)
- 0.8.5 Android connection issue [\#129](https://github.com/ably/ably-dotnet/issues/129)
- RealtimePresence possibly broken [\#114](https://github.com/ably/ably-dotnet/issues/114)

**Closed issues:**

- "The WebSocket protocol is not supported on this platform." on Windows 7 [\#165](https://github.com/ably/ably-dotnet/issues/165)
-  customer getting an exception from MessageWebSocketMessageReceivedEventArgs.GetDataReader\(\) in 0.8.6 beta 3 [\#164](https://github.com/ably/ably-dotnet/issues/164)

**Merged pull requests:**

- Fix duplicate Dependencies section in readme [\#170](https://github.com/ably/ably-dotnet/pull/170) ([withakay](https://github.com/withakay))
- fix for issue 167 [\#168](https://github.com/ably/ably-dotnet/pull/168) ([withakay](https://github.com/withakay))


## [0.8.6](https://github.com/ably/ably-dotnet/tree/0.8.6) (2017-11-28)

Changes since 0.8.5

**Implemented enhancements:**

- Add Release Process section to README [\#149](https://github.com/ably/ably-dotnet/issues/149)
- Per-instance loggers [\#33](https://github.com/ably/ably-dotnet/issues/33)
- Add documentation to README about which platforms are supported  [\#28](https://github.com/ably/ably-dotnet/issues/28)
- Populate message/presencemessage Id, ConnectionId, Timestamp [\#113](https://github.com/ably/ably-dotnet/issues/113)
- Fix HttpRequest & HttpRetry timeouts [\#95](https://github.com/ably/ably-dotnet/issues/95)
- Universal Windows Platform support [\#90](https://github.com/ably/ably-dotnet/issues/90)

**Fixed bugs:**

- System.Net.Http conflict [\#162](https://github.com/ably/ably-dotnet/issues/162)
- Xamarin 0.8.5+ System.NotImplementedException: The method or operation is not implemented.   at System.Net.Sockets.SocketAsyncEventArgs.FinishOperationAsyncFailur [\#161](https://github.com/ably/ably-dotnet/issues/161)
- iOS issue with MsWebsocket transport [\#151](https://github.com/ably/ably-dotnet/issues/151)
- Failure of history API call means channels no longer attach [\#116](https://github.com/ably/ably-dotnet/issues/116)
- Crash on android [\#109](https://github.com/ably/ably-dotnet/issues/109)
- When a Channel Fails to Resume After Disconnection, a Detached Event isn't Fired [\#108](https://github.com/ably/ably-dotnet/issues/108)
- Xamarin Android crash [\#96](https://github.com/ably/ably-dotnet/issues/96)
- Possible NewtonSoft incompatibility [\#91](https://github.com/ably/ably-dotnet/issues/91)
- authCallback does not support TokenRequest or token string [\#75](https://github.com/ably/ably-dotnet/issues/75)

**Closed issues:**

- Xamarin Null Reference Error - 0.8.6 - beta 2 [\#126](https://github.com/ably/ably-dotnet/issues/126)
- Concurrency issue in PresenceMap [\#124](https://github.com/ably/ably-dotnet/issues/124)
- Beta SDK depends on "Nito.AsyncEx" version="5.0.0-pre-02" [\#121](https://github.com/ably/ably-dotnet/issues/121)
- Failed to install Ably into a PCL project [\#120](https://github.com/ably/ably-dotnet/issues/120)
- Realtime chat example not working; issue with Nito.AsyncEX dep? [\#119](https://github.com/ably/ably-dotnet/issues/119)
- Request mac does not match [\#118](https://github.com/ably/ably-dotnet/issues/118)
- NullReference on iOS Xamarin [\#112](https://github.com/ably/ably-dotnet/issues/112)
- Token Authentication -  Unexpected error :Can not convert Object to String.; Code: 50000 [\#110](https://github.com/ably/ably-dotnet/issues/110)
- ConnectAsyncExtension.SocketConnectCompleted [\#105](https://github.com/ably/ably-dotnet/issues/105)
- ASP.NET Core support [\#93](https://github.com/ably/ably-dotnet/issues/93)
- Minor Crypto spec updates [\#38](https://github.com/ably/ably-dotnet/issues/38)

**Merged pull requests:**

- Remove websockets4net and Use System.Net.WebSockets exclusively  [\#163](https://github.com/ably/ably-dotnet/pull/163) ([withakay](https://github.com/withakay))
- Test improvements [\#160](https://github.com/ably/ably-dotnet/pull/160) ([withakay](https://github.com/withakay))
- Added presence features covering RTP2 and others [\#157](https://github.com/ably/ably-dotnet/pull/157) ([withakay](https://github.com/withakay))
- Use a regex to check the PATCH portion of the version string, [\#155](https://github.com/ably/ably-dotnet/pull/155) ([withakay](https://github.com/withakay))
- Update to allow building and packaging with Appveyor [\#154](https://github.com/ably/ably-dotnet/pull/154) ([withakay](https://github.com/withakay))
- Build script detects xunit runner [\#153](https://github.com/ably/ably-dotnet/pull/153) ([withakay](https://github.com/withakay))
- Update release process [\#152](https://github.com/ably/ably-dotnet/pull/152) ([withakay](https://github.com/withakay))
- Add Release Process [\#150](https://github.com/ably/ably-dotnet/pull/150) ([withakay](https://github.com/withakay))
- Use MSGPACK compiler flag to add/remove Protocol.MsgPack [\#148](https://github.com/ably/ably-dotnet/pull/148) ([withakay](https://github.com/withakay))
- Improve the XML Docs for AuthCallback [\#146](https://github.com/ably/ably-dotnet/pull/146) ([withakay](https://github.com/withakay))
- Make MsgPack optional and disabled by default [\#145](https://github.com/ably/ably-dotnet/pull/145) ([withakay](https://github.com/withakay))
- Test improvements & skip some tests. [\#144](https://github.com/ably/ably-dotnet/pull/144) ([withakay](https://github.com/withakay))
- simplified the message queue logic in ConnectionManager [\#142](https://github.com/ably/ably-dotnet/pull/142) ([withakay](https://github.com/withakay))
- Simplify now provider [\#140](https://github.com/ably/ably-dotnet/pull/140) ([withakay](https://github.com/withakay))
- Injectable logger [\#139](https://github.com/ably/ably-dotnet/pull/139) ([withakay](https://github.com/withakay))
- Injectable INowProvider [\#137](https://github.com/ably/ably-dotnet/pull/137) ([withakay](https://github.com/withakay))
- Updates to some test that rely on a delays [\#136](https://github.com/ably/ably-dotnet/pull/136) ([withakay](https://github.com/withakay))
- A fix to comply with RTL4b  [\#133](https://github.com/ably/ably-dotnet/pull/133) ([withakay](https://github.com/withakay))
- test AttachAwaitShouldtimeoutIfStateChanges would frequently fail [\#132](https://github.com/ably/ably-dotnet/pull/132) ([withakay](https://github.com/withakay))
- update the rest spec tests to use the InternalLogger [\#131](https://github.com/ably/ably-dotnet/pull/131) ([withakay](https://github.com/withakay))
- Removed obsolete assembly binding redirects from the Test project [\#130](https://github.com/ably/ably-dotnet/pull/130) ([withakay](https://github.com/withakay))
- Update README.md [\#128](https://github.com/ably/ably-dotnet/pull/128) ([withakay](https://github.com/withakay))
- Isolated logger for reliable testing [\#127](https://github.com/ably/ably-dotnet/pull/127) ([withakay](https://github.com/withakay))



## [0.8.6-beta4](https://github.com/ably/ably-dotnet/tree/0.8.6-beta4) (2017-11-24)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.6-beta3...0.8.6-beta4)

**Implemented enhancements:**

- Add Release Process section to README [\#149](https://github.com/ably/ably-dotnet/issues/149)
- Per-instance loggers [\#33](https://github.com/ably/ably-dotnet/issues/33)
- Add documentation to README about which platforms are supported  [\#28](https://github.com/ably/ably-dotnet/issues/28)

**Fixed bugs:**

- System.Net.Http conflict [\#162](https://github.com/ably/ably-dotnet/issues/162)
- Xamarin 0.8.5+ System.NotImplementedException: The method or operation is not implemented.   at System.Net.Sockets.SocketAsyncEventArgs.FinishOperationAsyncFailur [\#161](https://github.com/ably/ably-dotnet/issues/161)
- iOS issue with MsWebsocket transport [\#151](https://github.com/ably/ably-dotnet/issues/151)

**Merged pull requests:**

- Remove websockets4net and Use System.Net.WebSockets exclusively  [\#163](https://github.com/ably/ably-dotnet/pull/163) ([withakay](https://github.com/withakay))
- Test improvements [\#160](https://github.com/ably/ably-dotnet/pull/160) ([withakay](https://github.com/withakay))
- Added presence features covering RTP2 and others [\#157](https://github.com/ably/ably-dotnet/pull/157) ([withakay](https://github.com/withakay))
- Use a regex to check the PATCH portion of the version string, [\#155](https://github.com/ably/ably-dotnet/pull/155) ([withakay](https://github.com/withakay))

## [0.8.6-beta3](https://github.com/ably/ably-dotnet/tree/0.8.6-beta3)

[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.4.6...0.8.6-beta3)

**Implemented enhancements:**

- Populate message/presencemessage Id, ConnectionId, Timestamp [\#113](https://github.com/ably/ably-dotnet/issues/113)
- Fix HttpRequest & HttpRetry timeouts [\#95](https://github.com/ably/ably-dotnet/issues/95)
- Universal Windows Platform support [\#90](https://github.com/ably/ably-dotnet/issues/90)

**Fixed bugs:**

- Failure of history API call means channels no longer attach [\#116](https://github.com/ably/ably-dotnet/issues/116)
- Crash on android [\#109](https://github.com/ably/ably-dotnet/issues/109)
- When a Channel Fails to Resume After Disconnection, a Detached Event isn't Fired [\#108](https://github.com/ably/ably-dotnet/issues/108)
- Xamarin Android crash [\#96](https://github.com/ably/ably-dotnet/issues/96)
- Possible NewtonSoft incompatibility [\#91](https://github.com/ably/ably-dotnet/issues/91)
- authCallback does not support TokenRequest or token string [\#75](https://github.com/ably/ably-dotnet/issues/75)

**Closed issues:**

- Xamarin Null Reference Error - 0.8.6 - beta 2 [\#126](https://github.com/ably/ably-dotnet/issues/126)
- Concurrency issue in PresenceMap [\#124](https://github.com/ably/ably-dotnet/issues/124)
- Beta SDK depends on "Nito.AsyncEx" version="5.0.0-pre-02" [\#121](https://github.com/ably/ably-dotnet/issues/121)
- Failed to install Ably into a PCL project [\#120](https://github.com/ably/ably-dotnet/issues/120)
- Realtime chat example not working; issue with Nito.AsyncEX dep? [\#119](https://github.com/ably/ably-dotnet/issues/119)
- Request mac does not match [\#118](https://github.com/ably/ably-dotnet/issues/118)
- NullReference on iOS Xamarin [\#112](https://github.com/ably/ably-dotnet/issues/112)
- Token Authentication -  Unexpected error :Can not convert Object to String.; Code: 50000 [\#110](https://github.com/ably/ably-dotnet/issues/110)
- ConnectAsyncExtension.SocketConnectCompleted [\#105](https://github.com/ably/ably-dotnet/issues/105)
- ASP.NET Core support [\#93](https://github.com/ably/ably-dotnet/issues/93)
- Minor Crypto spec updates [\#38](https://github.com/ably/ably-dotnet/issues/38)

**Merged pull requests:**

- Update to allow building and packaging with Appveyor [\#154](https://github.com/ably/ably-dotnet/pull/154) ([withakay](https://github.com/withakay))
- Build script detects xunit runner [\#153](https://github.com/ably/ably-dotnet/pull/153) ([withakay](https://github.com/withakay))
- Update release process [\#152](https://github.com/ably/ably-dotnet/pull/152) ([withakay](https://github.com/withakay))
- Add Release Process [\#150](https://github.com/ably/ably-dotnet/pull/150) ([withakay](https://github.com/withakay))
- Use MSGPACK compiler flag to add/remove Protocol.MsgPack [\#148](https://github.com/ably/ably-dotnet/pull/148) ([withakay](https://github.com/withakay))
- Improve the XML Docs for AuthCallback [\#146](https://github.com/ably/ably-dotnet/pull/146) ([withakay](https://github.com/withakay))
- Make MsgPack optional and disabled by default [\#145](https://github.com/ably/ably-dotnet/pull/145) ([withakay](https://github.com/withakay))
- Test improvements & skip some tests. [\#144](https://github.com/ably/ably-dotnet/pull/144) ([withakay](https://github.com/withakay))
- simplified the message queue logic in ConnectionManager [\#142](https://github.com/ably/ably-dotnet/pull/142) ([withakay](https://github.com/withakay))
- Simplify now provider [\#140](https://github.com/ably/ably-dotnet/pull/140) ([withakay](https://github.com/withakay))
- Injectable logger [\#139](https://github.com/ably/ably-dotnet/pull/139) ([withakay](https://github.com/withakay))
- Injectable INowProvider [\#137](https://github.com/ably/ably-dotnet/pull/137) ([withakay](https://github.com/withakay))
- Updates to some test that rely on a delays [\#136](https://github.com/ably/ably-dotnet/pull/136) ([withakay](https://github.com/withakay))
- A fix to comply with RTL4b  [\#133](https://github.com/ably/ably-dotnet/pull/133) ([withakay](https://github.com/withakay))
- test AttachAwaitShouldtimeoutIfStateChanges would frequently fail [\#132](https://github.com/ably/ably-dotnet/pull/132) ([withakay](https://github.com/withakay))
- update the rest spec tests to use the InternalLogger [\#131](https://github.com/ably/ably-dotnet/pull/131) ([withakay](https://github.com/withakay))
- Removed obsolete assembly binding redirects from the Test project [\#130](https://github.com/ably/ably-dotnet/pull/130) ([withakay](https://github.com/withakay))
- Update README.md [\#128](https://github.com/ably/ably-dotnet/pull/128) ([withakay](https://github.com/withakay))
- Isolated logger for reliable testing [\#127](https://github.com/ably/ably-dotnet/pull/127) ([withakay](https://github.com/withakay))


## [0.8.5]

Upgraded Websocket transport with one that's compatible with netstandard1.3.


## [0.8.4.2]

Bug fix release. Added extra logging.

**Fixed bugs:**

- (partly) Xamarin Android crash [\#96](https://github.com/ably/ably-dotnet/issues/96)

## [0.8.4](https://github.com/ably/ably-dotnet/tree/0.8.4) (2016-06-27)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/0.8.3...0.8.4)

**Breaking changes**

- Renamed `StatsDataRequestQuery` to `StatsRequestParams`
- Updated properties of `ConnectionDetailsMessage`, `ErrorInfo`, `Message`, `PresenceMessage` to use the standard pascal casing used throughout .Net

**Implemented enhancements:**

- Added syncronous methods for `AblyAuth`, `AblyRest` and `RestChannel`
- Added the ability to specify the `LogLevel` and `LogHandler` through the ably `ClientOptions`
- Added extra logging when sending and receiving Http requests
- Added a `None` Log level to completely switch off logging
- Improved realtime presence handling
- Include version info in requests [\#82](https://github.com/ably/ably-dotnet/issues/82)
- Sync REST methods [\#80](https://github.com/ably/ably-dotnet/issues/80)
- Interoperability tests [\#83](https://github.com/ably/ably-dotnet/issues/83)
- Add documentation to README about which platforms are supported [\#28](https://github.com/ably/ably-dotnet/issues/28)

**Fixed bugs:**

- Various inconsistencies following review of documentation [\#81](https://github.com/ably/ably-dotnet/issues/81)
- Possible minor issues [\#68](https://github.com/ably/ably-dotnet/issues/68)
- Fixed the MsgPack dependency issue

## [0.8.3](https://github.com/ably/ably-dotnet/tree/0.8.3) (2016-06-27)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/v0.8.2-beta...0.8.3)

**Breaking changes**

- `ConnectionStateType` renamed to `ConnectionState`
- `PaginatedResult<>`. `NextQuery` renamed to `NextDataQuery`. `FirstQuery` -> `FirstDataQuery`

**Implemented enhancements:**

- `PaginatedResult<>` now supports `NextAsync` method which simplifies getting the results for the next page
- `Connection` and `RealtimeChannel` implement IEventEmitter interface and now match the ably [IDL](http://docs.ably.io/client-lib-development-guide/features/#idl)

**Fixed bugs:**

- NullReferenceException in ConnectionManager.cs [\#72](https://github.com/ably/ably-dotnet/issues/72)

## [v0.8.2-beta](https://github.com/ably/ably-dotnet/tree/v0.8.2-beta) (2016-06-10)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/v0.7.2...v0.8.2-beta)

**Implemented enhancements:**

- Add generic timeout for HTTP / Websocket requests [\#39](https://github.com/ably/ably-dotnet/issues/39)
- Add native async .NET support [\#26](https://github.com/ably/ably-dotnet/issues/26)
- Switch arity of auth methods [\#17](https://github.com/ably/ably-dotnet/issues/17)
- RealtimeClient [\#11](https://github.com/ably/ably-dotnet/issues/11)
- Realtime: Connection [\#10](https://github.com/ably/ably-dotnet/issues/10)
- Realtime: Channel [\#9](https://github.com/ably/ably-dotnet/issues/9)

**Fixed bugs:**

- Realtime constructor is blocking [\#41](https://github.com/ably/ably-dotnet/issues/41)
- Add generic timeout for HTTP / Websocket requests [\#39](https://github.com/ably/ably-dotnet/issues/39)
- Do not persist authorise attributes force & timestamp  [\#34](https://github.com/ably/ably-dotnet/issues/34)
- Class & namespace naming [\#29](https://github.com/ably/ably-dotnet/issues/29)
- Token Authentication not connecting [\#25](https://github.com/ably/ably-dotnet/issues/25)
- 0.8.x spec finalisation [\#20](https://github.com/ably/ably-dotnet/issues/20)
- Xamarin support [\#18](https://github.com/ably/ably-dotnet/issues/18)
- Switch arity of auth methods [\#17](https://github.com/ably/ably-dotnet/issues/17)

**Closed issues:**

- useBinaryProtocol=true memory leak? [\#66](https://github.com/ably/ably-dotnet/issues/66)
- Collection was modified exception [\#64](https://github.com/ably/ably-dotnet/issues/64)
- Attaching to a channel fails. [\#62](https://github.com/ably/ably-dotnet/issues/62)
- Custom AuthUrl gets called twice. [\#58](https://github.com/ably/ably-dotnet/issues/58)
- Presence timestamp init issue [\#56](https://github.com/ably/ably-dotnet/issues/56)
- UseBinaryProtocol causes presence data to become null [\#54](https://github.com/ably/ably-dotnet/issues/54)

**Merged pull requests:**

- Specs/realtime connection [\#57](https://github.com/ably/ably-dotnet/pull/57) ([marto83](https://github.com/marto83))
- Fix/issue 54 msgpack serialization [\#55](https://github.com/ably/ably-dotnet/pull/55) ([marto83](https://github.com/marto83))
- Specs/crypto [\#53](https://github.com/ably/ably-dotnet/pull/53) ([marto83](https://github.com/marto83))
- Specs/rest presence [\#52](https://github.com/ably/ably-dotnet/pull/52) ([marto83](https://github.com/marto83))
- Specs/rest channel [\#51](https://github.com/ably/ably-dotnet/pull/51) ([marto83](https://github.com/marto83))
- Specs/channels [\#50](https://github.com/ably/ably-dotnet/pull/50) ([marto83](https://github.com/marto83))
- Specs/auth [\#49](https://github.com/ably/ably-dotnet/pull/49) ([marto83](https://github.com/marto83))
- Specs/rest client [\#48](https://github.com/ably/ably-dotnet/pull/48) ([marto83](https://github.com/marto83))
- Validate rest idl [\#47](https://github.com/ably/ably-dotnet/pull/47) ([marto83](https://github.com/marto83))
- Fix tests and build [\#46](https://github.com/ably/ably-dotnet/pull/46) ([marto83](https://github.com/marto83))
- Standardise on 'initialized' for channel/connection state etc [\#42](https://github.com/ably/ably-dotnet/pull/42) ([SimonWoolf](https://github.com/SimonWoolf))
- Portable libs [\#35](https://github.com/ably/ably-dotnet/pull/35) ([Const-me](https://github.com/Const-me))
- RSC7a and RSC3 [\#31](https://github.com/ably/ably-dotnet/pull/31) ([Const-me](https://github.com/Const-me))
- Xamarin [\#30](https://github.com/ably/ably-dotnet/pull/30) ([yavor87](https://github.com/yavor87))

## [v0.7.2](https://github.com/ably/ably-dotnet/tree/v0.7.2) (2015-12-04)
[Full Changelog](https://github.com/ably/ably-dotnet/compare/v0.7.1...v0.7.2)

## [v0.7.1](https://github.com/ably/ably-dotnet/tree/v0.7.1) (2015-12-03)
**Implemented enhancements:**

- Add support for Nuget [\#14](https://github.com/ably/ably-dotnet/issues/14)
- Realtime: Channels [\#12](https://github.com/ably/ably-dotnet/issues/12)
- Spec validation [\#7](https://github.com/ably/ably-dotnet/issues/7)
- API changes Apr 2015 [\#4](https://github.com/ably/ably-dotnet/issues/4)

**Fixed bugs:**

- Token TTL  [\#22](https://github.com/ably/ably-dotnet/issues/22)
- TokenDetails serialisation [\#21](https://github.com/ably/ably-dotnet/issues/21)
- API changes Apr 2015 [\#4](https://github.com/ably/ably-dotnet/issues/4)

**Closed issues:**

- PaginatedResource API change  [\#3](https://github.com/ably/ably-dotnet/issues/3)

**Merged pull requests:**

- Fixed \#21. Forced TokenDetails dates to be serialized as milliseconds… [\#24](https://github.com/ably/ably-dotnet/pull/24) ([marto83](https://github.com/marto83))
- Realtime connection management redone [\#16](https://github.com/ably/ably-dotnet/pull/16) ([yavor87](https://github.com/yavor87))
- Token authentication for Realtime client [\#15](https://github.com/ably/ably-dotnet/pull/15) ([yavor87](https://github.com/yavor87))
- Realtime client [\#13](https://github.com/ably/ably-dotnet/pull/13) ([yavor87](https://github.com/yavor87))
- Realtime client initial [\#2](https://github.com/ably/ably-dotnet/pull/2) ([yavor87](https://github.com/yavor87))



\* *This Change Log was automatically generated by [github_changelog_generator](https://github.com/skywinder/Github-Changelog-Generator)*
