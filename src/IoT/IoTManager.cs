using System;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using Horimiya.Config;

namespace Horimiya.IoT
{
    public class IoTManager : IDisposable
    {
        private readonly IMqttClient _client;
        private readonly MqttClientOptions _options;
        private readonly MqttSettings _settings;

        public event EventHandler<string>? MessageReceived;

        public IoTManager(AppConfig config)
        {
            _settings = config.Mqtt ?? new MqttSettings();
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();
            
            _client.ApplicationMessageReceivedAsync += e =>
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? Array.Empty<byte>());
                MessageReceived?.Invoke(this, payload);
                return Task.CompletedTask;
            };

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.Host, _settings.Port);

            if (!string.IsNullOrWhiteSpace(_settings.Username))
                builder = builder.WithCredentials(_settings.Username, _settings.Password);

            if (_settings.UseTls)
                builder = builder.WithTls();

            _options = builder.Build();
        }

        public async Task StartAsync()
        {
            await _client.ConnectAsync(_options);
            var factory = new MqttFactory();
            var subOptions = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(f => {
                    f.WithTopic(_settings.SubscribeTopic);
                })
                .Build();
            await _client.SubscribeAsync(subOptions);
        }

        public async Task PublishAsync(string payload)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(_settings.PublishTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                .Build();
            await _client.PublishAsync(message);
        }

        public void Dispose()
        {
            if (_client.IsConnected)
                _client.DisconnectAsync().Wait();
            _client.Dispose();
        }
    }
}
