# JurassicWorld integration

Source: BCI_test's copy of https://github.com/Emotiv/unity-plugin (MIT).
Use Option 1 / desktop Cortex Service, without USE_EMBEDDED_LIB.

Local changes: EmotivUnityItf.Init binds singleton handlers once; WebsocketCortexClient.Close
disposes its retry timer and detaches socket events; RetryConnect synchronizes with shutdown;
DataStreamManager.Stop clears local session/buffer state for subsequent Play sessions.
BCI_test is unchanged. No application credentials are included.
