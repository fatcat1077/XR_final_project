from flask import Flask, request, jsonify
import whisper
import os
import tempfile

app = Flask(__name__)

MODEL_NAME = os.environ.get("WHISPER_MODEL", "base")
DEFAULT_LANGUAGE = os.environ.get("WHISPER_LANGUAGE", "").strip() or None
HOST = os.environ.get("STT_HOST", "0.0.0.0")
PORT = int(os.environ.get("STT_PORT", "5000"))

print(f"Loading Whisper model '{MODEL_NAME}'...")
model = whisper.load_model(MODEL_NAME)
print("Whisper model loaded.")

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"ok": True, "model": MODEL_NAME, "language": DEFAULT_LANGUAGE or "auto"})

@app.route("/stt", methods=["POST"])
def stt():
    if "file" not in request.files:
        return jsonify({"text": "", "error": "No file uploaded"}), 400

    file = request.files["file"]
    language = request.form.get("language", "").strip() or DEFAULT_LANGUAGE

    with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as temp_audio:
        temp_path = temp_audio.name
        file.save(temp_path)

    try:
        transcribe_options = {}
        if language:
            transcribe_options["language"] = language

        result = model.transcribe(temp_path, **transcribe_options)
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
    print(f"STT server listening on http://{HOST}:{PORT}/stt")
    app.run(host=HOST, port=PORT, debug=False, use_reloader=False)
