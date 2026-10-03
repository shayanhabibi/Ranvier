`review-comments.ps1` is copied without changes from
[roboz0r/comment-hygiene](https://github.com/roboz0r/comment-hygiene), plugin version 0.1.0.
Its MIT license is included alongside the script.

The pre-commit hook uses its F#-aware scanner to reject `FOR-REVIEW` comments in staged
`.fs`, `.fsi` and `.fsx` files. Markers inside string literals are permitted. Prose quality
still needs human or agent review.
