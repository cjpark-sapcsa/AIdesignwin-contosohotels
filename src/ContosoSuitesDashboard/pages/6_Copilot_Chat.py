import requests
import streamlit as st

st.set_page_config(layout="wide")

def get_api_endpoint():
    """Retrieve API endpoint and ensure it includes 'https://'."""
    try:
        api_endpoint = st.secrets["api"]["endpoint"].strip()
        if not api_endpoint.startswith("http"):
            api_endpoint = "https://" + api_endpoint.lstrip("/")

        # ✅ Ensure API base URL includes `/api/`
        if not api_endpoint.endswith("/api"):
            api_endpoint = api_endpoint.rstrip("/") + "/api"

        return api_endpoint
    except KeyError:
        st.error("🚨 API endpoint is missing in secrets.toml configuration.")
        return ""

def format_response(response_text):
    """Format AI response for better readability."""
    response_text = response_text.replace("\\n", "\n")  # ✅ Convert escaped newlines
    response_text = response_text.replace("\n\n", "\n")  # ✅ Remove extra blank lines

    # ✅ Convert numbered lists (1., 2., etc.) to bullet points (- )
    for i in range(1, 10):  # Handling up to 10 numbered points
        response_text = response_text.replace(f"{i}.", "-")

    return response_text.strip()  # ✅ Trim spaces for clean output

def send_message_to_copilot(message):
    """Send a message to the Copilot chat endpoint with proper error handling."""
    
    api_endpoint = get_api_endpoint()
    if not api_endpoint:
        return "❌ API configuration error."

    try:
        payload = {"message": message}  # ✅ Ensure correct JSON format
        headers = {"Content-Type": "application/json"}

        response = requests.post(f"{api_endpoint}/MaintenanceCopilotChat", json=payload, headers=headers, timeout=30)
        response.raise_for_status()

        # ✅ Try parsing response as JSON safely
        try:
            data = response.json()
            if isinstance(data, dict) and "message" in data:
                return format_response(data["message"])  # ✅ Apply formatting
            return format_response(response.text)  # ✅ Fallback: Clean raw response
        except ValueError:
            return format_response(response.text)  # ✅ Directly return API text response

    except requests.exceptions.Timeout:
        return "🚨 Timeout Error: The Copilot API took too long to respond."
    except requests.exceptions.ConnectionError:
        return "🚨 Connection Error: Cannot reach the Copilot API."
    except requests.exceptions.RequestException as e:
        return f"🚨 API Error: {str(e)}"

def main():
    """Main function for the Maintenance Copilot Chat Streamlit page."""

    st.write(
        """
        # 🏨 Maintenance Copilot Chat

        Welcome to the **Contoso Suites AI Maintenance Copilot**!  
        Use this chatbot to **submit maintenance requests** and track their status.

        ## ✨ Ask the Copilot a Question
        """
    )

    # Initialize chat history
    if "chat_messages" not in st.session_state:
        st.session_state.chat_messages = []

    # Display chat messages from history on app rerun
    for message in st.session_state.chat_messages:
        with st.chat_message(message["role"]):
            st.markdown(message["content"])

    # React to user input
    if prompt := st.chat_input("💬 How can I help you today?"):
        with st.spinner("🔄 Awaiting Copilot's response..."):
            st.chat_message("user").markdown(prompt)
            st.session_state.chat_messages.append({"role": "user", "content": prompt})

            # ✅ Send user message to Copilot
            response = send_message_to_copilot(prompt)

            # ✅ Apply formatting for readability
            formatted_response = format_response(response)

            # ✅ Display assistant response
            with st.chat_message("assistant"):
                st.markdown(formatted_response)

            # ✅ Store assistant response in chat history
            st.session_state.chat_messages.append({"role": "assistant", "content": formatted_response})

if __name__ == "__main__":
    main()
