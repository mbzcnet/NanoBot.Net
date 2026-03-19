#!/usr/bin/env python3
"""
OmniParser HTTP Server for NanoBot.Net

This server provides an HTTP API for OmniParser screen analysis.
It receives base64-encoded screenshots and returns parsed UI elements.

Usage:
    python server.py [--port PORT] [--weights-dir DIR]

Dependencies:
    pip install -r requirements.txt

OmniParser V2 Models:
    The server expects model weights in the weights/ directory.
    Download from: https://huggingface.co/microsoft/OmniParser-v2.0
    Or use: huggingface-cli download microsoft/OmniParser-v2.0 --local-dir weights
"""

import argparse
import base64
import io
import json
import logging
import os
import sys
import time
from pathlib import Path
from typing import Optional

from flask import Flask, jsonify, request
from PIL import Image

try:
    from omniparser import OmniParser
    from omniparser.table_parser import OmniparserTableParser
    OMNIPARSER_AVAILABLE = True
except ImportError:
    OMNIPARSER_AVAILABLE = False
    OmniParser = None
    OmniparserTableParser = None

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

app = Flask(__name__)

# Global parser instance
parser: Optional[OmniParser] = None
parser_config: dict = {}


def init_parser(weights_dir: Optional[str] = None):
    """Initialize the OmniParser instance."""
    global parser, parser_config

    if not OMNIPARSER_AVAILABLE:
        logger.error("OmniParser not available. Install with: pip install omniparser")
        return False

    try:
        # Determine weights directory
        if weights_dir is None:
            weights_dir = os.path.join(os.path.dirname(__file__), "weights")

        weights_path = Path(weights_dir)
        icon_detect_path = weights_path / "icon_detect"
        icon_caption_path = weights_path / "icon_caption_florence"

        parser_config = {
            "weights_dir": weights_dir,
            "icon_detect_path": str(icon_detect_path),
            "icon_caption_path": str(icon_caption_path),
        }

        # Check if weights exist
        if not icon_detect_path.exists() or not icon_caption_path.exists():
            logger.warning(f"Model weights not found at {weights_dir}")
            logger.warning("Download models from: https://huggingface.co/microsoft/OmniParser-v2.0")
            logger.warning("Or run: huggingface-cli download microsoft/OmniParser-v2.0 --local-dir weights")

        # Initialize parser with weights paths if available
        if icon_detect_path.exists() and icon_caption_path.exists():
            parser = OmniParser(
                icon_detect_model_path=str(icon_detect_path / "model.pt"),
                icon_caption_model_path=str(icon_caption_path / "model.safetensors")
            )
            logger.info(f"OmniParser initialized with weights from {weights_dir}")
        else:
            # Try to initialize without explicit weights (will try default locations)
            parser = OmniParser()
            logger.info("OmniParser initialized with default weights")

        return True

    except Exception as e:
        logger.error(f"Failed to initialize OmniParser: {e}")
        import traceback
        traceback.print_exc()
        return False


@app.route('/health', methods=['GET'])
def health():
    """Health check endpoint."""
    if parser is None:
        return jsonify({
            "status": "error",
            "message": "Parser not initialized",
            "omniparser_available": OMNIPARSER_AVAILABLE,
            "config": parser_config
        }), 503

    return jsonify({
        "status": "ok",
        "message": "OmniParser server is running",
        "omniparser_available": True,
        "config": parser_config
    })


@app.route('/parse', methods=['POST'])
def parse_screen():
    """
    Parse a screenshot and extract UI elements.

    Request body:
    {
        "image": "base64_encoded_image_data"
    }

    Response:
    {
        "annotated_image": "base64_encoded_annotated_image",
        "parsed_content": [
            {
                "bbox": [x1, y1, x2, y2],
                "label": "element description",
                "type": "button|input|icon|text|...",
                "text": "text content if applicable",
                "confidence": 0.95
            },
            ...
        ]
    }
    """
    if parser is None:
        return jsonify({"error": "Parser not initialized"}), 503

    try:
        data = request.get_json()
        if not data or 'image' not in data:
            return jsonify({"error": "Missing 'image' field in request body"}), 400

        image_data = data['image']

        # Decode base64 image
        try:
            image_bytes = base64.b64decode(image_data)
        except Exception as e:
            return jsonify({"error": f"Failed to decode base64 image: {e}"}), 400

        # Parse with OmniParser
        start_time = time.time()
        try:
            result = parser.parse(image_bytes)
        except Exception as e:
            logger.error(f"OmniParser parse error: {e}")
            return jsonify({"error": f"Parse error: {e}"}), 500

        parse_time = time.time() - start_time

        # Format response
        parsed_content = []
        annotated_image = None

        if result:
            if isinstance(result, dict):
                parsed_content = result.get('parsed_content', [])
                annotated_image = result.get('annotated_image')
            elif isinstance(result, list):
                parsed_content = result

        formatted_content = []
        for item in parsed_content:
            if isinstance(item, dict):
                formatted_content.append({
                    "bbox": item.get('bbox', []),
                    "label": item.get('label', ''),
                    "type": item.get('type', 'unknown'),
                    "text": item.get('text', ''),
                    "confidence": item.get('confidence', 0.0)
                })
            else:
                # Handle case where item might be a simple string or other type
                formatted_content.append({
                    "bbox": [],
                    "label": str(item) if item else '',
                    "type": "unknown",
                    "confidence": 0.0
                })

        response = {
            "annotated_image": annotated_image,
            "parsed_content": formatted_content,
            "metadata": {
                "parse_time_ms": round(parse_time * 1000, 2),
                "elements_found": len(formatted_content)
            }
        }

        logger.info(f"Parsed image with {len(formatted_content)} elements in {parse_time*1000:.1f}ms")
        return jsonify(response)

    except Exception as e:
        logger.error(f"Error processing request: {e}", exc_info=True)
        return jsonify({"error": str(e)}), 500


@app.route('/parse/simple', methods=['POST'])
def parse_screen_simple():
    """
    Simplified parse endpoint without annotated image.
    Faster for cases where only element locations are needed.
    """
    if parser is None:
        return jsonify({"error": "Parser not initialized"}), 503

    try:
        data = request.get_json()
        if not data or 'image' not in data:
            return jsonify({"error": "Missing 'image' field"}), 400

        image_bytes = base64.b64decode(data['image'])

        try:
            result = parser.parse(image_bytes, return_annotated_image=False)
        except Exception as e:
            logger.error(f"OmniParser parse error: {e}")
            return jsonify({"error": f"Parse error: {e}"}), 500

        parsed_content = []
        if result:
            if isinstance(result, dict):
                parsed_content = result.get('parsed_content', [])
            elif isinstance(result, list):
                parsed_content = result

        formatted_content = []
        for item in parsed_content:
            if isinstance(item, dict):
                formatted_content.append({
                    "bbox": item.get('bbox', []),
                    "label": item.get('label', ''),
                    "type": item.get('type', 'unknown'),
                    "confidence": item.get('confidence', 0.0)
                })

        return jsonify({
            "parsed_content": formatted_content,
            "elements_found": len(formatted_content)
        })

    except Exception as e:
        logger.error(f"Error in simple parse: {e}", exc_info=True)
        return jsonify({"error": str(e)}), 500


@app.route('/config', methods=['GET'])
def get_config():
    """Get current server configuration."""
    return jsonify({
        "parser_config": parser_config,
        "omniparser_available": OMNIPARSER_AVAILABLE,
        "parser_initialized": parser is not None
    })


def main():
    """Main entry point."""
    parser_arg = argparse.ArgumentParser(description='OmniParser HTTP Server for NanoBot.Net')
    parser_arg.add_argument('--port', type=int, default=18999,
                        help='Port to listen on (default: 18999)')
    parser_arg.add_argument('--host', type=str, default='127.0.0.1',
                        help='Host to bind to (default: 127.0.0.1)')
    parser_arg.add_argument('--weights-dir', type=str, default=None,
                        help='Path to model weights directory')
    parser_arg.add_argument('--debug', action='store_true',
                        help='Enable debug mode')
    parser_arg.add_argument('--lazy-init', action='store_true',
                        help='Delay parser initialization until first request')

    args = parser_arg.parse_args()

    # Set debug mode
    if args.debug:
        logging.getLogger().setLevel(logging.DEBUG)
        app.config['DEBUG'] = True

    # Initialize parser (unless lazy init is enabled)
    if not args.lazy_init:
        if not init_parser(args.weights_dir):
            logger.error("Failed to initialize OmniParser. Exiting.")
            if OMNIPARSER_AVAILABLE:
                logger.error("OmniParser package is installed but failed to initialize.")
                logger.error("This might be due to missing model weights.")
                logger.error("Please download models from: https://huggingface.co/microsoft/OmniParser-v2.0")
            sys.exit(1)

    # Run Flask app
    logger.info(f"Starting OmniParser server on {args.host}:{args.port}")
    app.run(host=args.host, port=args.port, debug=args.debug, threaded=True)


if __name__ == '__main__':
    main()
