#target photoshop

var index = 0;
var file;
var output = [];

var recursive = 0;

function init() {
	var Name = app.activeDocument.name.replace(/\.[^\.]+$/, '');
	file = new File(app.activeDocument.path + "/" + Name + ".json");
	file.open("w", "TEXT", "????");
	$.os.search(/windows/i) != -1 ? file.lineFeed = 'windows' : file.lineFeed = 'macintosh';

	file.writeln("{ " + getDataPair("Name", "UIGenerator", true) + getDataPair("version", "1.0", true) + "\"assets\":[");

	dumpLayers();

	printData();
	file.writeln("]\n}");
	file.close();
}


function printData() {
	var isOnCanvas = false;
	for (var i = 0; i < output.length; i++) {
		var line = output[i];
        var lastCanvas = output.length - 1;
        if (line.type == "canvas") {
			var code = "{ \"type\":\"canvas\", \"name\":\"" + line.layer.name + "\", \"layers\": [";
			file.writeln(getSpaces() + code);
			isOnCanvas = true;
		}
		else if (line.type == "endcanvas") {
			file.writeln(getSpaces() + "] }" + (i == lastCanvas ? "" : ","));
			isOnCanvas = false;
		} else {
			if (isOnCanvas) {
				addImage(line.layer, !(isOnCanvas && output[i + 1].type != "endcanvas"));
			} else {
				var code = "{ \"type\":\"image\", \"name\":\"" + line.layer.name + "\", \"layers\": [";
				file.writeln(getSpaces() + code);
				addImage(line.layer, true);
				file.writeln(getSpaces() + "] }" + (i == lastCanvas ? "" : ","));
			}
		}
	}
}

function dumpLayers() {
	var layerNum = app.activeDocument.layers.length;
	for (var i = 0; i < layerNum; i++) {
		var l = app.activeDocument.layers[i];

		if (l.layers !== undefined) {
			var isGroup = addGroup(l);
			dumpRecursive(l.layers, i == layerNum - 1);
			if (isGroup) {
				var canvas = { type: "endcanvas", layer: "" };
                output.push(canvas);
            }
		} else {
			addAsset(l, i == layerNum - 1);
		}
	}
}


function addGroup(layer) {
	var isGroup = false;
	if (checkCanvas(layer)) {
		var canvas = { type: "canvas", layer: layer };
		output.push(canvas);
		isGroup = true;
	} else if (checkFilename(layer)) {
		output.push({ type: "image", layer: layer });
	}
	return isGroup;
}

function dumpRecursive(l, last) {
	var items = [];
	for (var i = 0; i < l.length; i++) {
		var nl = l[i];
		if (nl.layers !== undefined) {
			var isGroup = addGroup(nl);
			dumpRecursive(nl.layers, i == l.length - 1);
            if (isGroup) {
				var canvas = { type: "endcanvas", layer: "" };
                output.push(canvas);
            }
		} else {
			addAsset(nl, i == l.length - 1);
		}
	}
}

function getSpaces() {
	var spaces = "";
	for (var i = 0; i < recursive; i++) {
		spaces += "   ";
	}
	return spaces;
}

function addAsset(item) {
	if (checkFilename(item)) {
		var canvas = { type: "image", layer: item };
		output.push(canvas);
	}
}

function addImage(item, last) {
	var b = item.bounds;
	var x = b[0];
	var y = b[1];
	var width = b[2] - b[0];
	var height = b[3] - b[1];
	var centerX = x + (width / 2);
	var centerY = y + (height / 2);
	var code = "{ \"type\":\"image\", \"name\":\"" + item.name + "\", \"x\":" + x.as('px') + ", \"y\":" + y.as('px') + ", \"width\":" + width.as('px') + ", \"height\":" + height.as('px') + ", \"centerX\":" + centerX.as('px') + ", \"centerY\":" + centerY.as('px') + "}" + (last ? "" : ",");
	file.writeln(getSpaces() + code);
}


function checkFilename(layer) {
	var isFilename = true;
	var png = layer.name.indexOf(".png");
	var jpg = layer.name.indexOf(".jpg");
	var pos = layer.name.indexOf(":POS");
	if (jpg < 0 && png < 0 && pos < 0) {
		isFilename = false;
	}
	return isFilename;
}

function checkCanvas(layer) {
	var isFilename = true;
	var canvas = layer.name.indexOf(":Canvas");
	if (canvas < 0) {
		isFilename = false;
	}
	return isFilename;
}


function getDataPair(name, value, comma) {
	var addComma = comma == true ? "," : "";
	return "\"" + name + "\" : \"" + value + "\"" + addComma;
}


init();
