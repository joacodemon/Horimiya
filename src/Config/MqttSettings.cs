using System;

namespace lospoderosos_lite.Config
{
    public class MqttSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string PublishTopic { get; set; }
        public string SubscribeTopic { get; set; }
        public bool UseTls { get; set; }
        public int QoS { get; set; }

        public MqttSettings()
        {
            Host = "test.mosquitto.org";
            Port = 1883;
            Username = "";
            Password = "";
            PublishTopic = "lospoderosos/commands";
            SubscribeTopic = "lospoderosos/status";
            UseTls = false;
            QoS = 0;
        }
    }
}
