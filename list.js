
const filePath = process.argv[2];

if (!filePath) {
  console.error("Please provide a file path as an argument.");
  process.exit(1);
}

// Dynamically import the file
import(filePath)
  .then((module) => {
  	console.log(JSON.stringify(module.default, null, 2));
  })
  .catch((err) => {
    console.error("Error importing file:", err);
  });
  
//  import exportedObject from './build/man.js'; // or './file.js' if using "type": "module"
//console.dir(exportedObject, { depth: null });