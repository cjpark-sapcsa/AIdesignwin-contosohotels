import requests
import streamlit as st

st.set_page_config(layout="wide")

def get_api_endpoint():
    """Retrieve API endpoint and ensure it includes 'https://', handling possible errors."""
    try:
        api_endpoint = st.secrets["api"]["endpoint"].strip()
        if not api_endpoint.startswith("http"):
            api_endpoint = "https://" + api_endpoint.lstrip("/")
        return api_endpoint
    except KeyError:
        st.error("API endpoint is missing in secrets.toml configuration.")
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
        st.error(f"Failed to fetch hotels: {e}")
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
        st.error(f"Failed to fetch bookings for hotel {hotel_id}: {e}")
        return []

@st.cache_data
def invoke_chat_endpoint(question):
    """Invoke the chat endpoint with the specified question and handle errors."""
    api_endpoint = get_api_endpoint()
    if not api_endpoint:
        return "Error processing request"
    try:
        response = requests.post(f"{api_endpoint}/Chat", data={"message": question}, timeout=30)
        response.raise_for_status()
        return response.text
    except requests.exceptions.RequestException as e:
        st.error(f"Chat endpoint request failed: {e}")
        return "Error processing request"

def main():
    """Main function for the Chat with Data Streamlit app."""

    st.write(
    """
    # API Integration via Semantic Kernel

    This Streamlit dashboard is intended to demonstrate how we can use
    the Semantic Kernel library to generate SQL statements from natural language
    queries and display them in a Streamlit app.

    ## Select a Hotel
    """
    )

    # Fetch and display hotels
    hotels_json = get_hotels()
    if not hotels_json:
        st.warning("No hotels found. Check API configuration.")
        return

    hotels = [{"id": hotel["hotelID"], "name": hotel["hotelName"]} for hotel in hotels_json]
    selected_hotel = st.selectbox("Hotel:", hotels, format_func=lambda x: x["name"])

    # Fetch and display bookings if a hotel is selected
    if selected_hotel:
        hotel_id = selected_hotel["id"]
        bookings = get_hotel_bookings(hotel_id)
        if bookings:
            st.write("### Bookings")
            st.table(bookings)
        else:
            st.warning("No bookings found for this hotel.")

    # Chat input section
    st.write(
        """
        ## Ask a Bookings Question

        Enter a question about hotel bookings in the text box below.
        Then select the "Submit" button to call the Chat endpoint.
        """
    )

    question = st.text_input("Question:", key="question")
    if st.button("Submit"):
        with st.spinner("Calling Chat endpoint..."):
            if question:
                response_text = invoke_chat_endpoint(question)
                st.write(response_text)
                st.success("Chat endpoint called successfully.")
            else:
                st.warning("Please enter a question.")

if __name__ == "__main__":
    main()
