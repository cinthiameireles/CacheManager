using System;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;

namespace CacheManager.Services
{
    public class CacheManagerService : CacheManager.CacheManagerService.CacheManagerServiceBase
    {
        private readonly ILogger<CacheManagerService> _logger;
        private readonly IConnectionMultiplexer _redis;

        public CacheManagerService(ILogger<CacheManagerService> logger, IConnectionMultiplexer redis)
        {
            _logger = logger;
            _redis = redis;
        }

        public override async Task<InsertResponse> InsertIntoCache(InsertRequest request, ServerCallContext context)
        {
            try
            {
                var db = _redis.GetDatabase();

                // Deserialize the bytes into a JSON string
                string jsonString = System.Text.Encoding.UTF8.GetString(request.Value.ToByteArray());

                // Store in Redis
                bool setResult = await db.StringSetAsync(request.Key, jsonString);

                if (setResult)
                {
                    return new InsertResponse
                    {
                        Success = true,
                        Message = "Object inserted into cache successfully."
                    };
                }
                else
                {
                    return new InsertResponse
                    {
                        Success = false,
                        Message = "Failed to insert object into cache."
                    };
                }
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
            var db = _redis.GetDatabase();
    
            // Obtém o valor associado à chave no Redis
            var value = await db.StringGetAsync(request.Key);

            if (value.IsNullOrEmpty)
            {
                // Se o valor não for encontrado, retorna um array de bytes vazio
                return new GetValueResponse { Value = Google.Protobuf.ByteString.Empty, Success = false };
            }

            // Converte o RedisValue para bytes e cria a resposta
            return new GetValueResponse { Value = Google.Protobuf.ByteString.CopyFromUtf8(value), Success = true };
        }
    }
}
