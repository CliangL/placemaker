// Module declarations for style and asset imports handled by webpack.
declare module "*.module.scss" {
    const classes: { readonly [key: string]: string };
    export default classes;
}
declare module "*.svg" {
    const url: string;
    export default url;
}
