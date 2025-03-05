using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ContosoSuitesWebAPI.Agents
{
    /// <summary>
    /// The maintenance copilot agent for assisting with maintenance requests.
    /// </summary>
    public class MaintenanceCopilot
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatCompletionService;
        private readonly ChatHistory _history; // ✅ Use `ChatHistory` instead of List<ChatMessageContent>

        public MaintenanceCopilot(Kernel kernel)
        {
            _kernel = kernel;
            _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            // ✅ Properly initialize ChatHistory (not List<ChatMessageContent>)
            _history = new ChatHistory("""
                You are a hotel maintenance copilot. Your job is to help customer service agents log maintenance requests.
                When the user provides an issue description, gather details like room number and hotel name.
                Ensure all necessary details are collected before submitting the request.
                Inform the user once the maintenance request has been logged in the system.
            """);
        }

        /// <summary>
        /// Chat with the maintenance copilot.
        /// </summary>
        public async Task<string> Chat(string userPrompt)
        {
            try
            {
                // ✅ Add user message to chat history correctly
                _history.AddUserMessage(userPrompt);

                var executionSettings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions  // ✅ Ensure AI can call functions
                };

                // ✅ Generate AI response using `ChatHistory`
                var response = await _chatCompletionService.GetChatMessageContentAsync(_history, executionSettings, _kernel);

                if (response == null || string.IsNullOrWhiteSpace(response.Content))
                {
                    return "⚠️ No valid response generated.";
                }

                // ✅ Store response in history for maintaining conversation
                _history.AddAssistantMessage(response.Content!);

                return response.Content!;
            }
            catch (Exception ex)
            {
                return $"⚠️ Error processing maintenance request: {ex.Message}";
            }
        }
    }
}
