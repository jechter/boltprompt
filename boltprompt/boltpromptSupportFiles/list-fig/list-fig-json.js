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

function mergeDeep(target, source) {
    for (const key in source) {
        if (source.hasOwnProperty(key)) {
            const sourceValue = source[key];
            const targetValue = target[key];

            if (Array.isArray(sourceValue)) {
                if (Array.isArray(targetValue)) {
                    target[key] = targetValue.concat(sourceValue);
                } else {
                    target[key] = sourceValue.slice();
                }
            } else if (
                typeof sourceValue === "object" && 
                sourceValue !== null && 
                !Array.isArray(sourceValue)
            ) {
                if (typeof targetValue === "object" && targetValue !== null) {
                    mergeDeep(targetValue, sourceValue);
                } else {
                    target[key] = {};
                    mergeDeep(target[key], sourceValue);
                }
            } else {
                target[key] = sourceValue;
            }
        }
    }
    return target;
}

import(filePath)
    .then((module) => {
        if (module.default.generateSpec != null)
        {
            module.default.generateSpec(null, executeShellCommand).then((spec) => {
                console.log(JSON.stringify(mergeDeep(module.default, spec), null, 2));      
            });
        }
        else
            console.log(JSON.stringify(module.default, null, 2));
    })
    .catch((err) => {
        console.error("Error importing file:", err);
    });
