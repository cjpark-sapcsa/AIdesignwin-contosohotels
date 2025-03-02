import streamlit as st
import requests
import json
import traceback  # ✅ Logs full error stack trace for error handling

# Suppress insecure HTTPS warnings for local dev/self-signed certs
import urllib3
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

st.set_page_config(layout="wide")

# ✅ API Endpoint from Streamlit Secrets
api_endpoint = st.secrets["api"]["endpoint"]

def handle_query_vectorization(query: str) -> list[float]:
    """
    Calls /Vectorize to convert the user's text query into a float vector.
    """
    try:
        response = requests.get(
            f"{api_endpoint}/Vectorize",
            params={"text": query},
            timeout=30,
            verify=False
        )

        if response.status_code == 200:
            vector = response.json()
            if isinstance(vector, list) and all(isinstance(x, (int, float)) for x in vector):
                return vector
            else:
                st.error("❌ /Vectorize returned invalid format.")
                return []
        else:
            st.error(f"❌ /Vectorize failed: {response.status_code}")
            return []
    except requests.exceptions.RequestException as e:
        st.error("🚨 API Error in /Vectorize")
        traceback.print_exc()
        return []

def handle_vector_search(query_vector_list: list[float], max_results: int, minimum_similarity_score: float):
    """
    Calls /VectorSearch with the vectorized query.
    """
    try:
        if not query_vector_list:
            st.error("🚨 Query vector is empty. Cannot proceed with /VectorSearch.")
            return None

        # ✅ Ensure valid JSON payload format
        payload = {
            "queryVector": [round(float(v), 6) for v in query_vector_list],  # Ensure floats are rounded to 6 decimals
            "maxResults": max_results,
            "minimumSimilarityScore": round(minimum_similarity_score, 2)  # Ensure float format
        }
        
        headers = {"Content-Type": "application/json"}

        response = requests.post(
            f"{api_endpoint}/VectorSearch",
            json=payload,
            headers=headers,
            timeout=30,
            verify=False
        )

        if response.status_code == 200:
            data = response.json()
            if isinstance(data, list):
                return data
            else:
                st.error("❌ /VectorSearch returned invalid format.")
                return None
        else:
            st.error(f"❌ /VectorSearch failed: {response.status_code}")
            return None
    except requests.exceptions.RequestException as e:
        st.error("🚨 API Error in /VectorSearch")
        traceback.print_exc()
        return None

def main():
    """
    Streamlit page for demonstrating vector search over maintenance requests.
    """
    st.title("Vector Search for Maintenance Requests")

    st.write(
        """
        1. Enter a search query describing a maintenance issue.
        2. Click "Submit" to vectorize your query and perform a vector search.
        3. The API returns matching maintenance requests with similarity scores.
        """
    )

    col1, col2 = st.columns(2)
    with col1:
        query = st.text_input("Search query:", key="query", value="")
    with col2:
        max_results = st.number_input("Max results (<=0 => all):", min_value=0, value=5)

    minimum_similarity_score = st.slider(
        "Minimum Similarity Score:",
        min_value=0.0,
        max_value=1.0,
        value=0.8,  # ✅ Reset to production value
        step=0.01
    )

    if st.button("Submit"):
        if not query.strip():
            st.warning("Please enter a valid query.")
            return

        with st.spinner("Performing vector search..."):
            try:
                # ✅ Step 1: Convert query to vector
                query_vector_list = handle_query_vectorization(query)

                if not query_vector_list:
                    st.error("🚨 No vector received from /Vectorize. Search cannot continue.")
                    return

                # ✅ Step 2: Call /VectorSearch with the vector
                results = handle_vector_search(query_vector_list, max_results, minimum_similarity_score)

                # ✅ Step 3: Display results
                if results:
                    st.write("## Search Results")
                    st.table(results)
                else:
                    st.error("🚨 No results returned from /VectorSearch.")
            except Exception as e:
                st.error("🚨 Unexpected error occurred.")
                traceback.print_exc()

if __name__ == "__main__":
    main()
