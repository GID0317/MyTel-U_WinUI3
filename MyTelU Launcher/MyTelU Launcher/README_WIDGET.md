# Windows Widget Integration Guide

You have successfully added the **In-App Schedule Widget** to your Home Page. 
The backend logic for the **Windows 11 Widget (Board)** is also implemented in `Services\WidgetProvider.cs`.

To fully enable the Windows Widget on the Widget Board, you need to register the COM server in your `Package.appxmanifest`. 

**WARNING**: Modifying the manifest incorrectly can prevent your app from launching.

### Steps to Enable Windows Widget:

1.  Open `Package.appxmanifest` as code (View Code).
2.  Locate the `<Extensions>` section inside `<Application>`.
3.  Add the following `com:Extension` and `uap3:Extension` blocks:

```xml
<Extensions>
    <!-- ... existing extensions ... -->

    <!-- API: Widget Provider COM Server Registration -->
    <com:Extension Category="windows.comServer">
        <com:ComServer>
            <com:ExeServer Executable="MyTelU Launcher.exe" DisplayName="MyTelU Widget Provider">
                <com:Class Id="D8948197-0130-4965-9877-66F53C3F8535" DisplayName="MyTelUWidgetProvider" />
            </com:ExeServer>
        </com:ComServer>
    </com:Extension>

    <!-- API: Widget Registration -->
    <uap3:Extension Category="windows.appExtension">
        <uap3:AppExtension Name="com.microsoft.windows.widgets" DisplayName="MyTelU Widgets" Id="MyTelUWidgets" PublicFolder="Public">
            <uap3:Properties>
                <WidgetProvider>
                    <ProviderIcons>
                        <Icon Path="Assets\StoreLogo.png" />
                    </ProviderIcons>
                    <Activation>
                        <CreateInstance ClassId="D8948197-0130-4965-9877-66F53C3F8535" />
                    </Activation>
                    <Definitions>
                        <Definition Id="ScheduleWidget" DisplayName="My Schedule" Description="View your daily schedule">
                            <Capabilities>
                                <Capability>
                                    <Size Name="medium" />
                                </Capability>
                                <Capability>
                                    <Size Name="large" />
                                </Capability>
                            </Capabilities>
                        </Definition>
                    </Definitions>
                </WidgetProvider>
            </uap3:Properties>
        </uap3:AppExtension>
    </uap3:Extension>
</Extensions>
```

4.  **Important**: You also need to ensure your `Program.cs` (or `App.xaml.cs` startup logic) registers the COM object factory for `WidgetProvider` when activated. Since this is an advanced topic involving `CsWinRT`, it is recommended to test the In-App widget first.

The In-App widget simulates the same logic by calling `ScheduleService` directly.
