// A Mocha reporter writing one JSON array of { path, state, message } to the file named by the
// `output` reporter option. `path` is the test's title path below the "Ranvier" root.
const fs = require("fs");
const Mocha = require("mocha");
const { EVENT_TEST_PASS, EVENT_TEST_FAIL, EVENT_RUN_END } = Mocha.Runner.constants;

class Reporter extends Mocha.reporters.Base {
    constructor(runner, options) {
        super(runner, options);
        const output = options.reporterOptions.output;
        const results = [];
        const record = (test, state, err) =>
            results.push({
                path: test.titlePath().slice(1),
                state,
                message: err ? String(err.message ?? err).split("\n")[0] : null,
            });
        runner.on(EVENT_TEST_PASS, test => record(test, "passed"));
        runner.on(EVENT_TEST_FAIL, (test, err) => record(test, "failed", err));
        runner.once(EVENT_RUN_END, () => fs.writeFileSync(output, JSON.stringify(results)));
    }
}

module.exports = Reporter;
