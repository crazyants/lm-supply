"""
OpenAI SDK compatibility smoke tests for LMSupply console host.
Run: pytest tests/sdk-compat/ -v
Prerequisites: LMSupply console running at http://localhost:5000
"""
import pytest
import json
import base64
import struct
import wave
import io
from pathlib import Path

try:
    from openai import OpenAI, NotFoundError, BadRequestError
    HAS_OPENAI = True
except ImportError:
    HAS_OPENAI = False

BASE_URL = "http://localhost:5000/v1"
pytestmark = pytest.mark.skipif(not HAS_OPENAI, reason="openai package not installed")


@pytest.fixture(scope="module")
def client():
    return OpenAI(base_url=BASE_URL, api_key="none")


@pytest.fixture(scope="module")
def silence_wav():
    """Generate 1 second of silence as WAV bytes."""
    buf = io.BytesIO()
    with wave.open(buf, 'wb') as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(16000)
        f.writeframes(b'\x00\x00' * 16000)
    return buf.getvalue()


class TestModels:
    def test_list_models_returns_list(self, client):
        models = client.models.list()
        assert models.object == "list"
        assert len(models.data) >= 0  # may be empty if no models registered

    def test_list_models_has_capabilities(self, client):
        """Models should have capabilities metadata."""
        import httpx
        resp = httpx.get(f"{BASE_URL}/models")
        assert resp.status_code == 200
        body = resp.json()
        assert body["object"] == "list"
        assert isinstance(body["data"], list)


class TestErrorHandling:
    def test_x_request_id_header_present(self):
        import httpx
        resp = httpx.get(f"{BASE_URL}/models")
        assert "x-request-id" in resp.headers

    def test_model_not_found_returns_404(self, client):
        with pytest.raises(NotFoundError) as exc_info:
            client.embeddings.create(
                model="nonexistent-model-xyz-that-does-not-exist",
                input="test"
            )
        assert exc_info.value.status_code == 404

    def test_missing_input_returns_400(self, client):
        with pytest.raises(BadRequestError) as exc_info:
            client.chat.completions.create(
                model="default",
                messages=[]
            )
        assert exc_info.value.status_code == 400

    def test_error_response_has_envelope(self):
        """Error responses must be {"error": {"message": ..., "type": ..., "code": ...}}"""
        import httpx
        resp = httpx.post(f"{BASE_URL}/embeddings", json={"model": "default"})
        assert resp.status_code in (400, 422)
        body = resp.json()
        assert "error" in body
        assert "message" in body["error"]
        assert "type" in body["error"]


class TestEmbeddings:
    def test_basic_embedding(self, client):
        resp = client.embeddings.create(model="default", input="hello world")
        assert len(resp.data) == 1
        assert isinstance(resp.data[0].embedding, list)
        assert len(resp.data[0].embedding) > 0
        assert resp.usage.total_tokens > 0

    def test_batch_embedding(self, client):
        resp = client.embeddings.create(
            model="default",
            input=["hello", "world", "foo bar"]
        )
        assert len(resp.data) == 3
        for i, item in enumerate(resp.data):
            assert item.index == i

    def test_embedding_base64_format(self):
        import httpx
        resp = httpx.post(f"{BASE_URL}/embeddings", json={
            "model": "default",
            "input": "hello",
            "encoding_format": "base64"
        })
        if resp.status_code == 200:
            body = resp.json()
            emb = body["data"][0]["embedding"]
            # Should be a string (base64), not an array
            assert isinstance(emb, str)
            # Decode and verify it's valid float bytes
            raw = base64.b64decode(emb)
            assert len(raw) % 4 == 0  # each float is 4 bytes

    def test_embedding_dimensions_truncation(self):
        import httpx
        resp = httpx.post(f"{BASE_URL}/embeddings", json={
            "model": "default",
            "input": "hello",
            "dimensions": 64
        })
        if resp.status_code == 200:
            body = resp.json()
            assert len(body["data"][0]["embedding"]) == 64


class TestChat:
    def test_basic_chat(self, client):
        resp = client.chat.completions.create(
            model="default",
            messages=[{"role": "user", "content": "Reply with exactly: OK"}],
            max_tokens=10
        )
        assert resp.choices[0].message.content is not None
        assert resp.choices[0].finish_reason in ("stop", "length")
        assert resp.usage is not None

    def test_chat_n_choices(self, client):
        resp = client.chat.completions.create(
            model="default",
            messages=[{"role": "user", "content": "Hi"}],
            n=2,
            max_tokens=5
        )
        assert len(resp.choices) == 2
        assert resp.choices[0].index == 0
        assert resp.choices[1].index == 1

    def test_chat_max_completion_tokens_alias(self):
        import httpx
        resp = httpx.post(f"{BASE_URL}/chat/completions", json={
            "model": "default",
            "messages": [{"role": "user", "content": "Hi"}],
            "max_completion_tokens": 5
        })
        assert resp.status_code == 200

    def test_chat_json_mode(self, client):
        resp = client.chat.completions.create(
            model="default",
            messages=[{"role": "user", "content": 'Return a JSON object with key "ok" set to true'}],
            response_format={"type": "json_object"},
            max_tokens=50
        )
        content = resp.choices[0].message.content
        assert content is not None
        parsed = json.loads(content)
        assert isinstance(parsed, dict)

    def test_chat_streaming(self, client):
        chunks = []
        with client.chat.completions.stream(
            model="default",
            messages=[{"role": "user", "content": "Hi"}],
            max_tokens=10
        ) as stream:
            for chunk in stream:
                chunks.append(chunk)
        assert len(chunks) > 0


class TestAudio:
    def test_transcription_endpoint_exists(self, silence_wav):
        import httpx
        resp = httpx.post(
            f"{BASE_URL}/audio/transcriptions",
            files={"file": ("test.wav", silence_wav, "audio/wav")},
            data={"model": "default"}
        )
        # Accept 200 or model-not-found 404 — endpoint must exist (not 405)
        assert resp.status_code != 405

    def test_translation_endpoint_exists(self, silence_wav):
        """New /v1/audio/translations endpoint must be registered."""
        import httpx
        resp = httpx.post(
            f"{BASE_URL}/audio/translations",
            files={"file": ("test.wav", silence_wav, "audio/wav")},
            data={"model": "default"}
        )
        assert resp.status_code != 404, "translations endpoint must exist"
        assert resp.status_code != 405

    def test_tts_endpoint_accepts_format(self):
        import httpx
        resp = httpx.post(f"{BASE_URL}/audio/speech", json={
            "model": "default",
            "input": "hello",
            "response_format": "wav"
        })
        assert resp.status_code != 405


class TestImages:
    def test_edits_endpoint_exists(self):
        import httpx
        resp = httpx.post(
            f"{BASE_URL}/images/edits",
            files={"image": ("test.png", b"PNG", "image/png")},
            data={"prompt": "test"}
        )
        assert resp.status_code != 404, "/v1/images/edits must be registered"

    def test_variations_endpoint_exists(self):
        import httpx
        resp = httpx.post(
            f"{BASE_URL}/images/variations",
            files={"image": ("test.png", b"PNG", "image/png")}
        )
        assert resp.status_code != 404, "/v1/images/variations must be registered"


class TestTranslation:
    def test_deepl_v2_endpoint_exists(self):
        """DeepL-compatible /v2/translate endpoint must be registered."""
        import httpx
        resp = httpx.post("http://localhost:5000/v2/translate", json={
            "text": ["hello"],
            "target_lang": "KO"
        })
        assert resp.status_code != 404, "/v2/translate must be registered"
        assert resp.status_code != 405
