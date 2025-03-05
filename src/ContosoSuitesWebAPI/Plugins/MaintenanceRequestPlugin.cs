using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ContosoSuitesWebAPI.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.SemanticKernel;

namespace ContosoSuitesWebAPI.Plugins
{
    /// <summary>
    /// The maintenance request plugin for creating and saving maintenance requests.
    /// </summary>
    public class MaintenanceRequestPlugin
    {
        private readonly CosmosClient _cosmosClient;
        private readonly Microsoft.Azure.Cosmos.Container _container;

        /// <summary>
        /// Constructor with dependency injection for CosmosClient.
        /// </summary>
        public MaintenanceRequestPlugin(CosmosClient cosmosClient)
        {
            _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
            var database = _cosmosClient.GetDatabase("ContosoSuites");
            _container = database.GetContainer("MaintenanceRequests");
        }

        /// <summary>
        /// Creates a new maintenance request for a hotel.
        /// </summary>
        [KernelFunction("create_maintenance_request")]
        [Description("Creates a new maintenance request for a hotel.")]
        public async Task<MaintenanceRequest> CreateMaintenanceRequest(Kernel kernel, int hotelId, string hotel, string details, int? roomNumber, string? location)
        {
            try
            {
                Console.WriteLine($"Creating a new maintenance request for hotel: {hotel}");

                var request = new MaintenanceRequest
                {
                    id = Guid.NewGuid().ToString(),
                    hotel_id = hotelId,
                    hotel = hotel,
                    details = details,
                    room_number = roomNumber,
                    source = "customer",
                    location = location
                };

                return request;
            }
            catch (Exception ex)
            {
                throw new Exception($"An error occurred while generating a new maintenance request: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves a maintenance request to the database for a hotel.
        /// </summary>
        [KernelFunction("save_maintenance_request")]
        [Description("Saves a maintenance request to the database for a hotel.")]
        public async Task SaveMaintenanceRequest(Kernel kernel, MaintenanceRequest maintenanceRequest)
        {
            try
            {
                if (maintenanceRequest == null)
                {
                    throw new ArgumentNullException(nameof(maintenanceRequest), "Maintenance request cannot be null.");
                }

                var partitionKey = new PartitionKey(maintenanceRequest.hotel_id.ToString());
                var response = await _container.CreateItemAsync(maintenanceRequest, partitionKey);
                Console.WriteLine($"Saved maintenance request {maintenanceRequest.id} with status {response.StatusCode}");
            }
            catch (CosmosException cosmosEx)
            {
                Console.WriteLine($"Cosmos DB error: {cosmosEx.StatusCode} - {cosmosEx.Message}");
                throw new Exception("Failed to save maintenance request to the database", cosmosEx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error in SaveMaintenanceRequest: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Handles chat interactions related to maintenance requests.
        /// </summary>
        [KernelFunction("chat")]
        [Description("Handles user chat input for maintenance requests.")]
        public async Task<string> Chat(string message)
        {
            try
            {
                return $"You asked about maintenance: {message}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Chat: {ex.Message}");
                throw new Exception("An error occurred while processing the chat request.", ex);
            }
        }


        /// <summary>
        /// Simulates AI processing of maintenance requests instead of echoing input.
        /// </summary>
        private async Task<string> GenerateMaintenanceResponse(Kernel kernel, string userMessage)
        {
            if (userMessage.ToLower().Contains("maintenance request"))
            {
                return "I can help with maintenance requests. Please provide hotel name, room number, and issue details.";
            }
            else if (userMessage.ToLower().Contains("status"))
            {
                return "I can check the status of a maintenance request. Please provide the request ID or room number.";
            }
            else
            {
                return "I'm here to assist with maintenance issues. Could you please clarify your request?";
            }
        }
    }
}
