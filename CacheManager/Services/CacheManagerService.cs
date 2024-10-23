using System;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;

namespace CacheManager.Services
{
    public class CacheManagerService : CacheManager.CacheManagerService.CacheManagerServiceBase
    {
        private readonly ILogger<CacheManagerService> _logger;
        private readonly IConnectionMultiplexer _redisConnection;
        private readonly RedLockFactory _redlockFactory;
        private readonly IDatabase _db;

        public CacheManagerService(ILogger<CacheManagerService> logger, IConnectionMultiplexer redis)
        {
            _logger = logger;
            _redisConnection = redis;
            _db = _redisConnection.GetDatabase();

            var redisEndpoints = new List<RedLockMultiplexer>
{
    new RedLockMultiplexer(ConnectionMultiplexer.Connect("127.0.0.1:6379"))
};

            // Criar o RedLockFactory com a conexão Redis
            _redlockFactory = RedLockFactory.Create(redisEndpoints);
        }

        public override async Task<InsertResponse> InsertIntoCache(InsertRequest request, ServerCallContext context)
        {
            try
            {
                // Deserialize the bytes into a JSON string
                byte[] bytes = request.Value.ToByteArray();

                // Store in Redis
                bool setResult = await _db.StringSetAsync(request.Key, bytes);

                if (!setResult)
                {
                    return new InsertResponse
                    {
                        Success = false,
                        Message = "Failed to insert object into cache."
                    };
                }

                // Publicar uma mensagem para notificar as outras aplicações que o valor foi inserido
                var channel = new RedisChannel($"key-update:{request.Key}", RedisChannel.PatternMode.Literal);
                var totalClients = await _redisConnection.GetSubscriber().PublishAsync(channel, "updated");

                Console.WriteLine($"Clientes que receberam a mensagem: {totalClients}");

                return new InsertResponse
                {
                    Success = true,
                    Message = "Object inserted into cache successfully."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting object into cache.");
                return new InsertResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        public override async Task<GetValueResponse> GetValueFromCache(GetValueRequest request, ServerCallContext context)
        {
            // Obtém o valor associado à chave no Redis
            var value = await _db.StringGetAsync(request.Key);

            if (value.IsNullOrEmpty)
            {
                // Tentar adquirir o lock usando Redlock
                var redlock = await _redlockFactory.CreateLockAsync(request.Key, TimeSpan.FromSeconds(30));

                if (redlock.IsAcquired)
                {
                    Console.WriteLine($"Pegou o lock de {request.Key}");
                    // Se conseguiu o lock, retornar nulo para o app buscar os dados
                    return new GetValueResponse { Value = Google.Protobuf.ByteString.Empty, Success = false };
                }

                Console.WriteLine($"Entrou na fila de espera de {request.Key}");
                // Caso não consiga o lock, a aplicação deve aguardar via Pub/Sub
                await SubscribeToKeyUpdate(request.Key);

                // Após a notificação, tentar o GET novamente
                value = await _db.StringGetAsync(request.Key);
                await redlock.DisposeAsync();
            }

            // Converte o RedisValue para bytes e cria a resposta
            return new GetValueResponse { Value = Google.Protobuf.ByteString.CopyFrom(value), Success = true };
        }

        // Método de inscrição Pub/Sub
        private async Task SubscribeToKeyUpdate(string key)
        {
            var subscriber = _redisConnection.GetSubscriber();
            var tcs = new TaskCompletionSource<bool>();

            // Definir explicitamente o modo Literal para o canal
            var channel = new RedisChannel($"key-update:{key}", RedisChannel.PatternMode.Literal);

            // Inscrever-se no canal de updates dessa chave
            await subscriber.SubscribeAsync(channel, (channel, message) =>
            {
                if (message == "updated")
                {
                    tcs.SetResult(true);
                }
            });

            // Aguardar até que a notificação seja recebida
            await tcs.Task;
        }

    }
}
