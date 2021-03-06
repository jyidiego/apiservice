using System;
using System.Collections.Generic;
using APIService.Handlers;
using APIService.Models;
using APIService.Repository;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace APIService.Services
{
  public class QueueService : IQueueService
  {
    private ConnectionFactory _connectionFactory;
    private IQueueConsumerService _queueConsumerService;
    private ILogger<QueueService> _logger; 
    private Dictionary<string,IMessageHandler> _handlers;
    private bool _processing;
    private static object _lock = new object();
    public QueueService(IQueueConsumerService queueConsumerService, ConnectionFactory rabbitConnection, ILoggerFactory loggerFactory)
    {
       _processing = false;
       _connectionFactory = rabbitConnection;
      
      _queueConsumerService = queueConsumerService;
      _logger = loggerFactory.CreateLogger<QueueService>();

      _queueConsumerService.QueueName = "test-queue";
      _queueConsumerService.ExchangeName ="ExchangeName";
      _queueConsumerService.ExchangeType = "direct";
      _queueConsumerService.RoutingKeyName = string.Empty;
      _queueConsumerService.Connect(_connectionFactory);
      
    }

    public void ProcessMessage(string message, IQueueConsumerService queueConsumerService, ulong deliveryTag, QueueMetric queueMetric)
    {
      var handlerFunc = ResolveHandler();
      if(handlerFunc.Invoke(message))
      {
        queueConsumerService.Model.BasicAck(deliveryTag, false);
        queueMetric.RoutingAction = RoutingAction.Processed;
        return;
      }
     
      this.RaiseException(new Exception("Message not processed."), queueConsumerService,deliveryTag,queueMetric);
     
    }

    private void RaiseException(Exception ex, IQueueConsumerService queueConsumerService, ulong deliveryTag, QueueMetric queueMetric)
    {
      queueConsumerService.Model.BasicNack(deliveryTag, false, false);
      queueMetric.RoutingAction = RoutingAction.Failed;

      _logger.LogError($"Error raised from QueueService: {ex.Message}");
    }

    public void ProcessQueue()
    {
      lock(_lock)
      {
        _logger.LogInformation("processor started");
        _processing = true;
        _queueConsumerService.ReadFromQueue(ProcessMessage, RaiseException,_queueConsumerService.ExchangeName,
          _queueConsumerService.QueueName,_queueConsumerService.RoutingKeyName);
      }
      
    }

    public void RegisterHandler(IMessageHandler handler)
    {
      if(_handlers == null)
        _handlers = new Dictionary<string, IMessageHandler>();

      _handlers.Add("myHandler",handler);
    }

    public void RegisterHandlers(IEnumerable<IMessageHandler> handlers)
    {
      foreach(var h in handlers)
      {
        _logger.LogInformation("handler registered");
        RegisterHandler(h);
      }
    }

    private Func<string, bool> ResolveHandler()
    {
      var m = _handlers["myHandler"];    
      return m.Handle;  
    }

    public bool IsProcessing()
    {
      return _processing;
    }
    
  }
}