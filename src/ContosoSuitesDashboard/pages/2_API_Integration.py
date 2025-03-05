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

@st.cache_data
def get_hotels():
    """Return a list of hotels from the API with error handling."""
    api_endpoint = get_api_endpoint()
    if not api_endpoint:
        return []
    try:
        response = requests.get(f"{api_endpoint}/Hotels", timeout=30)
        response.raise_for_status()
        return response.json()
    except requests.exceptions.RequestException as e:
        st.error(f"🚨 Failed to fetch hotels: {e}")
        return []

@st.cache_data
def get_hotel_bookings(hotel_id):
    """Return a list of bookings for the specified hotel with error handling."""
    api_endpoint = get_api_endpoint()
    if not api_endpoint:
        return []
    try:
        response = requests.get(f"{api_endpoint}/Hotels/{hotel_id}/Bookings", timeout=30)
        response.raise_for_status()
        return response.json()
    except requests.exceptions.RequestException as e:
        st.error(f"🚨 Failed to fetch bookings for hotel {hotel_id}: {e}")
        return []

def invoke_chat_endpoint(question):
    """Invoke the chat endpoint with the specified question and handle errors."""
    
    api_endpoint = get_api_endpoint()
    if not api_endpoint:
        return "❌ API configuration error."

    try:
        # ✅ Ensure `message` is properly formatted
        payload = {"message": question}
        headers = {"Content-Type": "application/json"}  # ✅ Fix: Use JSON format

        response = requests.post(f"{api_endpoint}/Chat", json=payload, headers=headers, timeout=30)
        response.raise_for_status()

        # ✅ Try parsing response as JSON; fallback to plain text
        try:
            data = response.json()
            if isinstance(data, dict):  # Ensure response is a dictionary
                return data.get("message", "⚠️ No valid response received.")  # Extract message key if available
            return f"⚠️ Unexpected API response format: {data}"  # Handle non-dictionary responses
        except ValueError:
            return f"⚠️ API returned invalid JSON: {response.text}"

    except requests.exceptions.Timeout:
        return "🚨 Timeout Error: The Chat API took too long to respond."
    except requests.exceptions.ConnectionError:
        return "🚨 Connection Error: Cannot reach the Chat API."
    except requests.exceptions.RequestException as e:
        return f"🚨 API Error: {str(e)}"

def main():
    """Main function for the Chat with Data Streamlit app."""

    st.write(
    """
    # 📊 API Integration via Semantic Kernel

    This Streamlit dashboard demonstrates how we can use
    the **Semantic Kernel library** to generate SQL statements from natural language
    queries and display them in a Streamlit app.

    ## 🏨 Select a Hotel
    """
    )

    # Fetch and display hotels
    hotels_json = get_hotels()
    if not hotels_json:
        st.warning("⚠️ No hotels found. Check API configuration.")
        return

    hotels = [{"id": hotel["hotelID"], "name": hotel["hotelName"]} for hotel in hotels_json]
    selected_hotel = st.selectbox("🏨 Hotel:", hotels, format_func=lambda x: x["name"])

    # Fetch and display bookings if a hotel is selected
    if selected_hotel:
        hotel_id = selected_hotel["id"]
        bookings = get_hotel_bookings(hotel_id)
        if bookings:
            st.write("### 🛏️ Bookings")
            st.table(bookings)
        else:
            st.warning("⚠️ No bookings found for this hotel.")

    # Chat input section
    st.write(
        """
        ## 💬 Ask a Bookings Question

        Enter a question about hotel bookings in the text box below.
        Then select the "Submit" button to call the Chat endpoint.
        """
    )

    question = st.text_input("📝 Question:", key="question")
    if st.button("🚀 Submit"):
        if not question.strip():
            st.warning("⚠️ Please enter a question.")
            return

        with st.spinner("🔄 Calling Chat API..."):
            response_text = invoke_chat_endpoint(question)

            # ✅ Handle empty responses
            if not response_text.strip():
                response_text = "⚠️ No response received from AI."

            st.write("### 🤖 AI Response")
            st.success(response_text)

if __name__ == "__main__":
    main()
