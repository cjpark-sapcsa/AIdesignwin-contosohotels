import requests
import streamlit as st

st.set_page_config(layout="wide")

def send_message_to_copilot(message):
    """Send a message to the Copilot chat endpoint with proper error handling."""
    
    api_endpoint = st.secrets["api"]["endpoint"]
    
    # Ensure API URL has "https://" prefix
    if not api_endpoint.startswith("http"):
        api_endpoint = "https://" + api_endpoint

    try:
        response = requests.post(f"{api_endpoint}/MaintenanceCopilotChat", json={"message": message}, timeout=60)
        response.raise_for_status()  # Raise an error for HTTP errors
        return response.text
    except requests.exceptions.RequestException as e:
        return f"❌ Error connecting to Copilot: {str(e)}"

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
            # Display user message in chat message container
            st.chat_message("user").markdown(prompt)

            # Add user message to chat history
            st.session_state.chat_messages.append({"role": "user", "content": prompt})

            # Send user message to Copilot and get response
            response = send_message_to_copilot(prompt)
            
            # ✅ Format AI response for better readability
            formatted_response = (
                response.replace("\\n", "\n")  # Convert escaped newlines
                        .replace("1.", "- ")   # Convert numbered lists to bullets
                        .replace("2.", "- ")
                        .replace("3.", "- ")
                        .replace("4.", "- ")
            )

            # Display assistant response in chat message container
            with st.chat_message("assistant"):
                st.markdown(formatted_response)

            # Add assistant response to chat history
            st.session_state.chat_messages.append({"role": "assistant", "content": formatted_response})

if __name__ == "__main__":
    main()
