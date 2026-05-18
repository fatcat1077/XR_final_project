from flask import Flask, request, jsonify
import whisper
import os
import tempfile

app = Flask(__name__)

print("Loading Whisper model...")
model = whisper.load_model("base")
print("Whisper model loaded.")

@app.route("/stt", methods=["POST"])
def stt():
    if "file" not in request.files:
        return jsonify({"text": "", "error": "No file uploaded"}), 400

    file = request.files["file"]

    with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as temp_audio:
        temp_path = temp_audio.name
        file.save(temp_path)

    try:
        result = model.transcribe(temp_path, language="en")
        text = result.get("text", "").strip()

        print(f"[Whisper] text = {text}")

        return jsonify({"text": text})
    except Exception as e:
        print(f"[Whisper] Error: {e}")
        return jsonify({"text": "", "error": str(e)}), 500
    finally:
        if os.path.exists(temp_path):
            os.remove(temp_path)

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000, debug=True)