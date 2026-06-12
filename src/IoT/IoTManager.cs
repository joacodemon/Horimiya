using System;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using lospoderosos_lite.Config;

namespace lospoderosos_lite.IoT
{
    public class IoTManager : IDisposable
    {
        private readonly IMqttClient _client;
        private readonly IMqttClientOptions _options;
        private readonly MqttSettings _settings;

        public event EventHandler<string>? MessageReceived;

        public IoTManager(AppConfig config)
        {
            _settings = config.Mqtt ?? new MqttSettings();
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();
            _client.UseApplicationMessageReceivedHandler(e =>
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload ?? Array.Empty<byte>());
                MessageReceived?.Invoke(this, payload);
            });

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
            await _client.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(_settings.SubscribeTopic)
                .WithExactlyOnceQoS()
                .Build());
        }

        public async Task PublishAsync(string payload)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(_settings.PublishTopic)
                .WithPayload(payload)
                .WithExactlyOnceQoS()
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
