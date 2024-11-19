const filePath = process.argv[2];

if (!filePath) {
    console.error("Please provide a file path as an argument.");
    process.exit(1);
}

async function executeShellCommand({ command, args }) {
    return new Promise((resolve, reject) => {
        resolve({ stdout: "", stderr: "we don't support shell execution" });
    });
}

// Dynamically import the file
import(filePath)
    .then((module) => {
        if (module.default.generateSpec != null)
        {
            module.default.generateSpec(null, executeShellCommand).then((spec) => {
                console.log(JSON.stringify({ ...module.default, ...spec}, null, 2));      
            });
        }
        else
            console.log(JSON.stringify(module.default, null, 2));
    })
    .catch((err) => {
        console.error("Error importing file:", err);
    });
