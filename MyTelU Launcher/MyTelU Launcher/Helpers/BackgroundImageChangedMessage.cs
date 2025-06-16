using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyTelU_Launcher.Helpers;
public class BackgroundImageChangedMessage : ValueChangedMessage<string>
{
    public BackgroundImageChangedMessage(string value) : base(value)
    {
    }
}
