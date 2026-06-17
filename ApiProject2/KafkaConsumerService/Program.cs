using KafkaConsumerService;
using KafkaConsumerService.Events;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IEventProcessingService, EventProcessingService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
